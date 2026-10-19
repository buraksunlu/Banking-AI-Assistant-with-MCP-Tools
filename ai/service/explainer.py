"""Turns a question and backend facts into a Turkish explanation.

AI-04: the call goes through LangChain's ChatOllama. The prompt is still the AI-03 placeholder; AI-05 writes
the real prompt and AI-06 validates the output. The endpoint only depends on the ``Explainer`` protocol.
"""

import json
import time
from typing import Protocol

import httpx
from langchain_ollama import ChatOllama
from ollama import ResponseError

from contracts import Fact
from ollama_client import (
    ModelNotFound,
    OllamaBadResponse,
    OllamaError,
    OllamaSettings,
    OllamaTimeout,
    OllamaUnavailable,
)

TEMPORARY_SYSTEM_PROMPT = (
    "Sen bir bankacılık harcama asistanısın. Sana kullanıcının sorusu ve backend'in hesapladığı hazır veri "
    "verilecek. Soruyu 2-3 cümlelik sade Türkçe ile cevapla. Yalnız verideki bilgileri ve rakamları, "
    "verildiği yazımla aynen kullan; hesap yapma, yeni rakam ekleme."
)

# A 2-3 sentence Turkish answer is about 150-200 tokens; 400 leaves room and caps a runaway answer at ~17 s.
MAX_ANSWER_TOKENS = 400


class Explainer(Protocol):
    """Anything that can explain backend facts in Turkish."""

    def explain(self, question: str, facts: list[Fact]) -> str: ...


def build_messages(question: str, facts: list[Fact]) -> list[dict]:
    """The chat messages sent to the model: a system prompt and one user message with the facts."""
    lines = "\n".join(f"- {fact.label}: {fact.text}" for fact in facts)
    return [
        {"role": "system", "content": TEMPORARY_SYSTEM_PROMPT},
        {"role": "user", "content": f"Soru: {question}\nVeri:\n{lines}"},
    ]


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

    def explain(self, question: str, facts: list[Fact]) -> str:
        """Returns the model's answer within the time limit; failures surface as ``OllamaError`` subclasses."""
        # ChatOllama always streams, so the client timeout never ends an answer that keeps coming.
        # Reading the parts ourselves and closing the stream at the deadline also makes Ollama stop (AI-02).
        deadline = time.monotonic() + self.settings.timeout_seconds
        parts: list[str] = []
        done_reason = None
        try:
            stream = self.llm.stream(build_messages(question, facts))
            try:
                for chunk in stream:
                    parts.append(chunk.content if isinstance(chunk.content, str) else "")
                    done_reason = chunk.response_metadata.get("done_reason") or done_reason
                    if time.monotonic() > deadline:
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
