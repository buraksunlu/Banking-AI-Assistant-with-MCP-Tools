"""Which tools a chat turn shows the model (CHAT-01).

With eleven tools every description is read on every turn: adding the budget tools moved older questions to the wrong tool
or period (MCP-17/18 eval, 53/63) until their descriptions were patched. The router keeps the planning tools away from every
other question. Decisions: K1-C (word rules first, the model only when no rule matches), K2-C (two groups: the nine tools of
MCP-08, measured 48/48 on their own, and the two planning tools; K2-B's three smaller groups broke "Geçen ayki en büyük
harcamalarım" and "Peki geçen ay?" on every run), K3-A (the general group when the model cannot tell), K5-B (clear
out-of-scope requests are answered without a model), K6-A (a follow-up's own rule first, else the last call's group).

The rules work on folded text (answer_check.fold_turkish: every i-like letter is "i"); the tool names are the MCP server's.
"""

import re
import time
from dataclasses import dataclass
from typing import Protocol

from answer_check import fold_turkish
from ollama_client import OllamaError

GENERAL, PLANNING = "general", "planning"

# A tool the table does not know is shown in every group (narrow): a tool added on the server must not vanish.
GROUPS = {
    GENERAL: ("get_spending_summary", "get_category_breakdown", "get_merchant_spending", "get_merchant_breakdown",
              "get_highest_transactions", "compare_spending", "get_monthly_trend", "get_recurring_payments",
              "get_upcoming_payments"),
    PLANNING: ("check_budget", "simulate_savings"),
}

# The model's answer (intent-v1 names three kinds of question); spending and payments share the general group.
_MODEL_GROUPS = {"spending": GENERAL, "payments": GENERAL, "planning": PLANNING}

# K5-B: only requests that are clearly not about the user's spending; a question about transfers is not one of them.
_OUT_OF_SCOPE = re.compile(
    r"\btransfer\s+(?:yap|et|gönder)|\bpara\s+gönder|\bhavale\s+(?:yap|et)|\beft\s+(?:yap|et)"
    r"|\bhisse|\byatirim\s+yap|\bkripto|\bborsa|\bfikra|\bşaka")

# A planning word decides even when spending words are there too ("Starbucks harcamamı yarıya indirsem" is a savings question).
_PLANNING_WORDS = re.compile(
    r"bütçe|\blimit|\bayir(?:dim|acağim|sam)|tasarruf|biriktir|\bazalt|\bkis(?:sa|ar|al)|yariya|\bindirs|\bindirir|çeyre")
# Spending and payment words: the general group without asking the model.
_GENERAL_WORDS = re.compile(
    r"harca|\bnereye|kategori|işyer|karşilaştir|\b(?:aya|aylara|döneme)\s+göre|\baylara\b|\bdeğiş"
    r"|\ben\s+(?:büyük|pahali|yüksek|çok)|\bverdim|\bödedim|\bgitti|alişveriş|\bmasraf|\bne\s+kadar"
    r"|abonel|düzenli|yaklaşan|önümüzdeki|\böde(?:me|yece|necek)|\bödüyor|sabit\s+gider")

# The model's question is a shortcut: a slow answer falls back to the general group instead of eating the turn.
INTENT_SECONDS = 5.0


@dataclass(frozen=True)
class Route:
    """The groups whose tools the model is shown, or ``out_of_scope``; ``source`` says how they were found ("rule",
    "previous", "model" or "fallback") for the eval and the log."""

    groups: tuple[str, ...]
    out_of_scope: bool = False
    source: str = "rule"

    def tools(self) -> frozenset[str]:
        """The names of the groups' tools."""
        return frozenset(name for group in self.groups for name in GROUPS[group])


class IntentClassifier(Protocol):
    """Asks the model which group a message belongs to; returns one of the group names."""

    def classify(self, message: str, deadline: float) -> str: ...


def group_of(tool: str) -> str | None:
    """The group a tool belongs to; None for a tool the table does not know."""
    return next((group for group, tools in GROUPS.items() if tool in tools), None)


def narrow(tools: list[dict], route: Route) -> list[dict]:
    """The listed tools of the route's groups, in the server's order, plus every tool the table does not know."""
    allowed = route.tools()
    return [t for t in tools if t["function"]["name"] in allowed or group_of(t["function"]["name"]) is None]


class Router:
    """Chooses the groups of one message (K1-C); ``classifier`` is only asked when no rule matches."""

    def __init__(self, classifier: IntentClassifier | None = None, clock=time.monotonic):
        self.classifier, self.clock = classifier, clock

    def route(self, message: str, previous_tool: str | None, deadline: float) -> Route:
        """The route of ``message``; ``previous_tool`` is the last call's tool when the message is a follow-up (CHAT-02)."""
        folded = fold_turkish(message)
        if _OUT_OF_SCOPE.search(folded):
            return Route((), out_of_scope=True)
        if _PLANNING_WORDS.search(folded):
            return Route((PLANNING,))
        if _GENERAL_WORDS.search(folded):
            return Route((GENERAL,))
        # "Peki geçen ay?" says only what changed: it stays with the last call's tools (K6-A).
        if previous_tool is not None and (group := group_of(previous_tool)) is not None:
            return Route((group,), source="previous")
        return self._ask(message, deadline)

    def _ask(self, message: str, deadline: float) -> Route:
        if self.classifier is not None:
            try:
                intent = self.classifier.classify(message, min(deadline, self.clock() + INTENT_SECONDS))
                if intent in _MODEL_GROUPS:
                    return Route((_MODEL_GROUPS[intent],), source="model")
            except OllamaError:
                pass
        return Route((GENERAL,), source="fallback")
