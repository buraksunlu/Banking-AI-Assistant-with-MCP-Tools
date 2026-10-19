"""Turns a question and backend facts into a Turkish explanation.

AI-04: the call goes through LangChain's ChatOllama. AI-05: the prompt lives in prompts.py (versioned).
AI-06: every answer is checked against the facts; a rejected answer gets one correction, then it is refused.
The endpoint only depends on the ``Explainer`` protocol.
"""

import json
import logging
import time
from typing import Protocol

import httpx
from langchain_ollama import ChatOllama
from ollama import ResponseError

from answer_check import AnswerRejected, check_answer
from contracts import Fact
from prompts import build_messages, build_retry_messages
from ollama_client import (
    ModelNotFound,
    OllamaBadResponse,
    OllamaError,
    OllamaSettings,
    OllamaTimeout,
    OllamaUnavailable,
)

logger = logging.getLogger("bankingai.ai")

# A 2-3 sentence Turkish answer is about 150-200 tokens; 400 leaves room and caps a runaway answer at ~17 s.
MAX_ANSWER_TOKENS = 400


class Explainer(Protocol):
    """Anything that can explain backend facts in Turkish."""

    def explain(self, question: str, facts: list[Fact]) -> str: ...


class LangChainExplainer:
    """Explainer that calls the local model through LangChain's ChatOllama."""

    def __init__(self, settings: OllamaSettings):
        self.settings = settings
        self.llm = ChatOllama(
            model=settings.model,
            base_url=settings.base_url,
            reasoning=False,  # sent as think: false (AI-01: thinking is too slow on this machine)
            temperature=0,
            num_predict=MAX_ANSWER_TOKENS,
            # Per read, not in total: it only fires when no part arrives at all (e.g. while the model loads).
            client_kwargs={"timeout": settings.timeout_seconds},
        )
        # Replaceable in tests, so time limits can be checked without waiting.
        self.clock = time.monotonic

    def explain(self, question: str, facts: list[Fact]) -> str:
        """Returns a checked answer within the time limit.

        Raises ``AnswerRejected`` when the answer fails the check twice (or once, with no time left to correct it),
        and ``OllamaError`` subclasses when the model cannot be reached or does not answer in time.
        """
        started = self.clock()
        # One limit for the whole explanation (K5-B): a correction uses what the first attempt left over.
        deadline = started + self.settings.timeout_seconds

        answer = self._ask(build_messages(question, facts), deadline)
        check = check_answer(question, facts, answer)
        if check.ok:
            return answer

        first_attempt = self.clock() - started
        if deadline - self.clock() < first_attempt:
            raise AnswerRejected(f"Rejected with no time left for a correction ({check.describe()}).")

        corrected = self._ask(build_retry_messages(question, facts, answer, check), deadline)
        second_check = check_answer(question, facts, corrected)
        if second_check.ok:
            logger.info("Answer accepted after one correction (first attempt: %s).", check.describe())
            return corrected
        raise AnswerRejected(f"Rejected after one correction ({second_check.describe()}).")

    def _ask(self, messages: list[dict], deadline: float) -> str:
        """Streams one answer and stops at ``deadline``; failures surface as ``OllamaError`` subclasses."""
        # ChatOllama always streams, so the client timeout never ends an answer that keeps coming.
        # Reading the parts ourselves and closing the stream at the deadline also makes Ollama stop (AI-02).
        parts: list[str] = []
        done_reason = None
        try:
            stream = self.llm.stream(messages)
            try:
                for chunk in stream:
                    parts.append(chunk.content if isinstance(chunk.content, str) else "")
                    done_reason = chunk.response_metadata.get("done_reason") or done_reason
                    if self.clock() > deadline:
                        raise OllamaTimeout(f"No complete answer from Ollama within {self.settings.timeout_seconds:g} s.")
            finally:
                stream.close()
        except OllamaError:
            raise
        except Exception as error:
            translated = self._translate(error)
            if translated is None:
                raise
            raise translated from None

        if done_reason == "length":
            # A sentence cut in the middle is worse than no sentence: the caller shows the plain result instead.
            raise OllamaBadResponse(f"Answer was cut off at the {MAX_ANSWER_TOKENS} token limit.")
        return "".join(parts).strip()

    def _translate(self, error: Exception) -> OllamaError | None:
        """Maps LangChain/Ollama/httpx errors to the four service errors; None for anything else (a real bug)."""
        if isinstance(error, ResponseError):
            if error.status_code == 404:
                return ModelNotFound(f"Model {self.settings.model!r} is not installed on {self.settings.base_url}.")
            if error.status_code >= 500:
                return OllamaUnavailable(f"Ollama failed ({error.status_code}): {error.error}")
            return OllamaBadResponse(f"Ollama rejected the request ({error.status_code}): {error.error}")
        if isinstance(error, httpx.TimeoutException):
            return OllamaTimeout(f"No answer from Ollama within {self.settings.timeout_seconds:g} s.")
        if isinstance(error, (httpx.TransportError, ConnectionError)):
            return OllamaUnavailable(f"Cannot reach Ollama at {self.settings.base_url}: {error}")
        if isinstance(error, json.JSONDecodeError):
            # Only this ValueError: a broader catch would hide a real bug behind "bad response".
            return OllamaBadResponse(f"Ollama answer line is not JSON: {error}")
        return None
