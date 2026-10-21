"""One chat turn: the model picks a tool, the backend computes, the model explains (AI-14a, MCP-b, CHAT-03, CHAT-02).

    waiting question? → tools (MCP server) → tool selection (model, with the last call when the message is a follow-up) →
    grounding (code) → tool call (MCP server checks and computes) → facts (code) → explanation (model) → last call kept

A period, count or figure (budget amount, currency, cut; MCP-17/18) the user did not say, or a merchant name that fits several
merchants, is asked back instead of guessed (CHAT-03); the reply completes the remembered call without a second selection, and
a figure still unsaid after it is asked next. A short follow-up ("Peki geçen ay?") is read
with the last call; an argument must then be in the message or carried over unchanged (CHAT-02). The answer's figures are
never carried over: every turn is computed again. The whole turn shares one time limit. The
user's token only goes to the MCP server (and, as a digest, keys the waiting question), never to a model.
"""

import logging
import time
from dataclasses import dataclass
from typing import Protocol

from langchain_ollama import ChatOllama

from clarification import (Pending, PendingStore, ask_amount, ask_count, ask_currency, ask_merchant, ask_percent, ask_period,
                           check_question, resolve, template)
from contracts import Fact
from conversation import ConversationStore, LastCall, is_follow_up
from explainer import stream_message
from facts import outcome_for
from grounding import check_arguments
from ollama_client import OllamaError, OllamaSettings
from prompts import CLARIFY_TEXT, OUT_OF_SCOPE_TEXT, build_clarify_messages, build_tool_messages
from tools import ToolArgumentsRejected, ToolGateway

logger = logging.getLogger("bankingai.ai")


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
    """Lets the model choose among the given tools for a message."""

    def select(self, message: str, tools: list[dict], deadline: float, context: LastCall | None = None) -> list[ToolCall]: ...


# K8-A: a tool call is 30-60 tokens. Without a cap, a model that writes a text answer instead (a joke) can run past the
# turn's limit; that text is never shown (the fixed out-of-scope reply is), so cutting it loses nothing.
MAX_TOOL_CALL_TOKENS = 200
# A one-sentence question with two or three options is 40-80 tokens (CHAT-03 probe: 1,3-2,0 s).
MAX_QUESTION_TOKENS = 120
# The question is a nicety: when the model is slow, the fixed question is shown instead of keeping the user waiting.
QUESTION_SECONDS = 8.0


class ClarificationWriter(Protocol):
    """Words a question back with the given options (CHAT-03 K4-B)."""

    def write(self, question: str, kind: str, options: list[str], numbered: bool, deadline: float, asked: str = "") -> str: ...


class LangChainToolSelector:
    """Tool selection with the local model through LangChain's tool calling."""

    def __init__(self, settings: OllamaSettings):
        self.settings = settings
        # Same model settings as the explainer (AI-01/04): no thinking, deterministic.
        self.llm = ChatOllama(
            model=settings.model, base_url=settings.base_url, reasoning=False, temperature=0, keep_alive=settings.keep_alive,
            num_predict=MAX_TOOL_CALL_TOKENS, client_kwargs={"timeout": settings.timeout_seconds},
        )
        self.clock = time.monotonic

    def select(self, message: str, tools: list[dict], deadline: float, context: LastCall | None = None) -> list[ToolCall]:
        """The tools the model chose (``context``: the previous call of a follow-up); an empty list when it answered in text."""
        # Bound per turn: the list comes from the MCP server on every turn, with the user's token (K1-A).
        reply = stream_message(self.llm.bind_tools(tools), build_tool_messages(message, context), deadline, self.clock, self.settings)
        return [ToolCall(call["name"], call["args"]) for call in reply.tool_calls]


class LangChainClarificationWriter:
    """The local model words the question; the chat checks it and falls back to the fixed one (K4-B)."""

    def __init__(self, settings: OllamaSettings):
        self.settings = settings
        self.llm = ChatOllama(
            model=settings.model, base_url=settings.base_url, reasoning=False, temperature=0, keep_alive=settings.keep_alive,
            num_predict=MAX_QUESTION_TOKENS, client_kwargs={"timeout": settings.timeout_seconds},
        )
        self.clock = time.monotonic

    def write(self, question: str, kind: str, options: list[str], numbered: bool, deadline: float, asked: str = "") -> str:
        """The question as the model words it (not yet checked); ``asked`` is the merchant text that matched several."""
        messages = build_clarify_messages(question, kind, options, numbered, asked)
        return str(stream_message(self.llm, messages, deadline, self.clock, self.settings).content).strip()


class ChatService:
    """Runs one chat turn."""

    def __init__(self, selector: ToolSelector, gateway: ToolGateway, explainer, settings: OllamaSettings,
                 writer: ClarificationWriter | None = None, waiting: PendingStore | None = None,
                 history: ConversationStore | None = None):
        self.selector, self.gateway, self.explainer, self.settings = selector, gateway, explainer, settings
        # Without a writer every question is the fixed one (tests, and the fallback of K4-B).
        self.writer = writer
        self.waiting = waiting or PendingStore()
        self.history = history or ConversationStore()
        self.clock = time.monotonic

    def answer(self, message: str, token: str) -> ChatReply:
        """The reply to ``message`` for the user whose token is given."""
        # One limit for the whole turn: listing, selection, tool call and explanation all take from it (AI-06 K5-B, AI-14).
        deadline = self.clock() + self.settings.timeout_seconds

        # A reply to the question asked last time completes that call: no listing, no selection (K3-A). Anything else is a
        # new question, and the old one is dropped either way (K5-A).
        waiting = self.waiting.take(token)
        if waiting is not None:
            arguments = resolve(waiting, message)
            if arguments is not None:
                question = f"{waiting.question} ({message})"
                # "3000" answered the amount; the currency the model guessed as well is asked next, not run.
                later = tuple(name for name in waiting.later if name not in arguments)
                if later:
                    return self._ask(_ask_figure(waiting.tool, arguments, later[0], later[1:], question), [], token, deadline)
                return self._run(ToolCall(waiting.tool, arguments), question, token, deadline)

        # Listed first: when the tools cannot answer, the single local model is not kept busy for nothing.
        tools = self.gateway.list_tools(token, deadline)
        # Only a short or "peki…" message is read with the last call (K4-B); a full question stands on its own, as before.
        # The follow-up itself is what is explained and kept: a growing chain of questions led both the model (back to an
        # older merchant) and the explanation ("geçen ay" for this month) astray in the live trial.
        context = self.history.recall(token) if is_follow_up(message) else None
        calls = self.selector.select(message, tools, deadline, context)
        if not calls:
            # The model's own text is never shown here: it might promise a transfer it cannot make (K5-A).
            return ChatReply("outOfScope", OUT_OF_SCOPE_TEXT, [], [])
        if len(calls) > 1:
            # Running only one would show half an answer as a whole one (K2-A); several tools come with CHAT-04.
            return ChatReply("clarification", CLARIFY_TEXT, calls, [])

        call = calls[0]
        schemas = {tool["function"]["name"]: tool["function"].get("parameters", {}) for tool in tools}
        if call.tool not in schemas:
            return ChatReply("clarification", CLARIFY_TEXT, calls, [])

        # A period or count the user did not say is the model's guess: asked back, the tool does not run (K1-C, K2-A).
        grounding = check_arguments(call.arguments, schemas[call.tool], message, context.arguments if context else None)
        if grounding.missing is not None:
            return self._ask(self._question_for(call, grounding.unsaid, schemas[call.tool], message), [], token, deadline)
        return self._run(ToolCall(call.tool, grounding.arguments), message, token, deadline)

    def _run(self, call: ToolCall, question: str, token: str, deadline: float) -> ChatReply:
        try:
            # The MCP server checks the arguments against the schema it published (MCP-a K3-A); there is no second copy here.
            result = self.gateway.call(call.tool, call.arguments, token, deadline)
        except ToolArgumentsRejected:
            # The same fixed question as before MCP (MCP-b K3-A): the user sees no difference.
            return ChatReply("clarification", CLARIFY_TEXT, [call], [])
        outcome = outcome_for(call.tool, call.arguments, result)
        used = [call]
        if outcome.candidates:
            # The backend found several of the customer's merchants: one of them is chosen by the user, never here.
            pending = ask_merchant(call.tool, call.arguments, call.arguments.get("merchant", ""), list(outcome.candidates), question)
            return self._ask(pending, used, token, deadline)
        if outcome.reply is not None:
            kind, text = outcome.reply
            return ChatReply(kind, text, used, outcome.facts)

        answer = self.explainer.explain(question, outcome.facts, deadline=deadline)
        # Kept only after an explained answer: the call, never its figures (CHAT-02 K2-A).
        self.history.remember(token, LastCall(call.tool, call.arguments, question))
        return ChatReply("answer", answer, used, outcome.facts)

    @staticmethod
    def _question_for(call: ToolCall, unsaid: tuple[str, ...], schema: dict, message: str) -> Pending:
        argument, later = unsaid[0], unsaid[1:]
        # Every guessed value is left out of the stored call, so none of them runs once the user has answered.
        known = {k: v for k, v in call.arguments.items() if k not in unsaid}
        if argument == "period":
            return ask_period(call.tool, known, message)
        if argument in ("amount", "currencyCode", "reductionPercent"):
            return _ask_figure(call.tool, known, argument, later, message)
        rule = schema.get("properties", {}).get(argument, {})
        return ask_count(call.tool, known, argument, rule.get("minimum", 1), rule.get("maximum", 12), message)

    def _ask(self, pending: Pending, used: list[ToolCall], token: str, deadline: float) -> ChatReply:
        text = template(pending)
        # A figure question stays the fixed text: a worded one could suggest an amount or a cut (MCP-17/18).
        if self.writer is not None and pending.model_worded:
            try:
                written = self.writer.write(pending.question, pending.kind, [o.label for o in pending.options], pending.numbered,
                                            min(deadline, self.clock() + QUESTION_SECONDS), pending.asked)
                if check_question(written, pending):
                    text = written
                else:
                    # Only the kind is logged: the wording may repeat the user's words.
                    logger.info("Model question failed the check (%s); the fixed question is used", pending.kind)
            except OllamaError as error:
                logger.info("No model question (%s); the fixed question is used", type(error).__name__)
        self.waiting.put(token, pending)
        return ChatReply("clarification", text, used, [])


def _ask_figure(tool: str, arguments: dict, argument: str, later: tuple[str, ...], question: str) -> Pending:
    """The question for one of the user's own figures (MCP-17/18), with the ones still to ask after it."""
    asker = {"amount": ask_amount, "currencyCode": ask_currency, "reductionPercent": ask_percent}[argument]
    return asker(tool, arguments, question, later=later)
