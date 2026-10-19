"""Turns a question and backend facts into a Turkish explanation (AI-03).

The prompt here is a temporary placeholder: AI-04 moves the call to LangChain, AI-05 writes the real
prompt and AI-06 validates the output. The endpoint only depends on the ``Explainer`` protocol.
"""

from typing import Protocol

from contracts import Fact
from ollama_client import OllamaSettings, chat

TEMPORARY_SYSTEM_PROMPT = (
    "Sen bir bankacılık harcama asistanısın. Sana kullanıcının sorusu ve backend'in hesapladığı hazır veri "
    "verilecek. Soruyu 2-3 cümlelik sade Türkçe ile cevapla. Yalnız verideki bilgileri ve rakamları, "
    "verildiği yazımla aynen kullan; hesap yapma, yeni rakam ekleme."
)


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


class OllamaExplainer:
    """Explainer that calls the local model through ``ollama_client``."""

    def __init__(self, settings: OllamaSettings):
        self.settings = settings

    def explain(self, question: str, facts: list[Fact]) -> str:
        """Returns the model's answer; Ollama failures surface as ``OllamaError`` subclasses."""
        return chat(self.settings, build_messages(question, facts)).content.strip()
