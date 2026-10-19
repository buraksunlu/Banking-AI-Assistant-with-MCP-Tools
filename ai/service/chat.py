"""One chat turn: the model picks a tool, the backend computes, the model explains (AI-14a).

    message → tool selection (model) → argument check (code) → tool call (bridge) → facts (code) → explanation (model)

The whole turn shares one time limit. The user's token only goes to the bridge, never to a model.
"""

import time
from dataclasses import dataclass
from typing import Protocol

from langchain_ollama import ChatOllama

from contracts import Fact
from explainer import stream_message
from facts import outcome_for
from ollama_client import OllamaSettings
from prompts import CLARIFY_TEXT, OUT_OF_SCOPE_TEXT, build_tool_messages
from tools import tool_definitions, validate_arguments


@dataclass(frozen=True)
class ToolCall:
    """A tool and its arguments, as the model chose them (and, after the check, as they were run)."""

    tool: str
    arguments: dict


@dataclass(frozen=True)
class ChatReply:
    """The answer of one turn; ``type`` is "answer", "clarification" or "outOfScope"."""

    type: str
    answer: str
    tool_calls: list[ToolCall]
    facts: list[Fact]


class ToolSelector(Protocol):
    """Lets the model choose tools for a message."""

    def select(self, message: str, deadline: float) -> list[ToolCall]: ...


class LangChainToolSelector:
    """Tool selection with the local model through LangChain's tool calling."""

    def __init__(self, settings: OllamaSettings):
        self.settings = settings
        # Same model settings as the explainer (AI-01/04): no thinking, deterministic.
        self.llm = ChatOllama(
            model=settings.model, base_url=settings.base_url, reasoning=False, temperature=0,
            client_kwargs={"timeout": settings.timeout_seconds},
        ).bind_tools(tool_definitions())
        self.clock = time.monotonic

    def select(self, message: str, deadline: float) -> list[ToolCall]:
        """The tools the model chose; an empty list when it answered in text instead."""
        reply = stream_message(self.llm, build_tool_messages(message), deadline, self.clock, self.settings)
        return [ToolCall(call["name"], call["args"]) for call in reply.tool_calls]


class ChatService:
    """Runs one chat turn."""

    def __init__(self, selector: ToolSelector, gateway, explainer, settings: OllamaSettings):
        self.selector, self.gateway, self.explainer, self.settings = selector, gateway, explainer, settings
        self.clock = time.monotonic

    def answer(self, message: str, token: str) -> ChatReply:
        """The reply to ``message`` for the user whose token is given."""
        # One limit for the whole turn: selection, API call and explanation all take from it (AI-06 K5-B, AI-14).
        deadline = self.clock() + self.settings.timeout_seconds

        calls = self.selector.select(message, deadline)
        if not calls:
            # The model's own text is never shown here: it might promise a transfer it cannot make (K5-A).
            return ChatReply("outOfScope", OUT_OF_SCOPE_TEXT, [], [])
        if len(calls) > 1:
            # Running only one would show half an answer as a whole one (K2-A); several tools come with CHAT-04.
            return ChatReply("clarification", CLARIFY_TEXT, calls, [])

        call = calls[0]
        arguments = validate_arguments(call.tool, call.arguments)
        if arguments is None:
            return ChatReply("clarification", CLARIFY_TEXT, calls, [])

        result = self.gateway.call(call.tool, arguments, token, deadline)
        outcome = outcome_for(call.tool, arguments, result)
        used = [ToolCall(call.tool, arguments)]
        if outcome.reply is not None:
            kind, text = outcome.reply
            return ChatReply(kind, text, used, outcome.facts)

        answer = self.explainer.explain(message, outcome.facts, deadline=deadline)
        return ChatReply("answer", answer, used, outcome.facts)
