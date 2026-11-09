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

from answer_check import fold_rule, fold_turkish
from ollama_client import OllamaError

GENERAL, PLANNING, WEALTH = "general", "planning", "wealth"

# A tool the table does not know is shown in every group (narrow): a tool added on the server must not vanish.
GROUPS = {
    GENERAL: ("get_spending_summary", "get_category_breakdown", "get_merchant_spending", "get_merchant_breakdown",
              "get_highest_transactions", "compare_spending", "get_monthly_trend", "get_recurring_payments",
              "get_upcoming_payments"),
    PLANNING: ("check_budget", "simulate_savings"),
    # INC-03 K3-A: income, advice and the what-if scenarios; reached by word rules only (K16-A: the model never picks this group).
    WEALTH: ("get_income_share", "get_spending_advice", "what_if_one_off", "what_if_monthly"),
}

# The model's answer (intent-v1 names three kinds of question); spending and payments share the general group.
_MODEL_GROUPS = {"spending": GENERAL, "payments": GENERAL, "planning": PLANNING}

# K5-B: only requests that are clearly not about the user's spending; a question about transfers is not one of them.
_OUT_OF_SCOPE = re.compile(fold_rule(
    r"\btransfer\s+(?:yap|et|gönder)|\bpara\s+gönder|\bhavale\s+(?:yap|et)|\beft\s+(?:yap|et)"
    r"|\bhisse|\byatirim\s+yap(?!sa[yd])|\bkripto|\bborsa|\bfikra|\bşaka"))

# INC-03 K5-A: a request for advice or a forecast ("altın alayım mı", "dolar yükselir mi"). Answered with a fixed text and no model:
# the backend computes the past, nobody here may say what to buy or what will happen. "yatirim yap…" is the out-of-scope rule's.
_ADVICE_REQUEST = re.compile(fold_rule(
    # CHAT-09 K15-A: "alayım" in any shape ("alayım mı", "X mı Y mı alayım", "hangisini alayım") asks what to buy.
    r"\balayim\b|\balmali\s+miyim|\bhangisini\s+al|\bmantikli\s+mi|\btavsiye"
    r"|\byüksel(?:ir|ecek)|\bdüş(?:er|ecek)\s+mi|\bkazandirir\s+mi|\bartar\s+mi"))
# INC-03 K3-A, K4-A: income share, saving advice and the "what if" scenarios. Checked before the planning and general words,
# which they contain too ("maaşımın ne kadarını harcadım", "tasarruf", "biriktirseydim"); only -seydim/-saydım and "X olarak"
# make a saving a what-if, "biriktiririm" stays planning.
_WEALTH_WORDS = re.compile(fold_rule(
    r"\bmaaş|\bgelir|\bn(?:erede|ereden|elerden)\s+tasarruf|\btasarruf\s+öneri"
    r"|(?:sa|se)ydi(?:m|k)\b|\b(?:dolar|euro|altin)\s+(?:olarak|cinsinden)"))

# CHAT-09 K5-D: inside the wealth group, a what-if ("-seydim/-saydım", "X olarak") whose words name one scenario is shown only that
# scenario's tool: spending words mean last period's spending bought at once, "ayda/her ay/aylık" a monthly saving. Both or neither
# leaves the group as it is. The 4b model sent "Geçen ayki harcamalarımı biriktirseydim" to the monthly saving.
_WHAT_IF = re.compile(fold_rule(r"(?:sa|se)ydi(?:m|k)\b|\b(?:dolar|euro|altin)\s+(?:olarak|cinsinden)"))
_ONE_OFF_WORDS = re.compile(fold_rule(r"harca|\bmasraf|\bgider"))
_MONTHLY_WORDS = re.compile(fold_rule(r"\bayda\b|\bher\s+ay\b|\baylık|\bdüzenli"))
ONE_OFF, MONTHLY = "what_if_one_off", "what_if_monthly"

# A planning word decides even when spending words are there too ("Starbucks harcamamı yarıya indirsem" is a savings question).
_PLANNING_WORDS = re.compile(fold_rule(
    r"bütçe|\blimit|\bayir(?:dim|acağim|sam)|tasarruf|biriktir|\bazalt|\bkis(?:sa|ar|al)|yariya|\bindirs|\bindirir|çeyre"
    # CHAT-09 K14-A: "sınır" folds to "sinir" (nerve), so only a limit's own shapes: set, or followed by a figure or "aş…".
    r"|\bsınır\s+(?:koy|belirle)|\bsınır(?:ı|ım|ımı)?\s+(?:\d|aş)"))
# Spending and payment words: the general group without asking the model.
_GENERAL_WORDS = re.compile(fold_rule(
    r"harca|\bnereye|kategori|işyer|karşilaştir|\b(?:aya|aylara|döneme)\s+göre|\baylara\b|\bdeğiş"
    r"|\ben\s+(?:büyük|pahali|yüksek|çok)|\bverdim|\bödedim|\bgitti|alişveriş|\bmasraf|\bne\s+kadar"
    r"|abonel|düzenli|yaklaşan|önümüzdeki|\böde(?:me|yece|necek)|\bödüyor|sabit\s+gider"))

# CHAT-08 K6-C: words that make a message a question about spending; with them and no period, "which period?" is asked instead of
# the out-of-scope reply (a "ne kadar" alone could be a balance question).
_SPENDING_WORDS = re.compile(fold_rule(r"harca|\bverdim|\bödedim|\bgitti|alişveriş|\bmasraf"))

# The model's question is a shortcut: a slow answer falls back to the general group instead of eating the turn.
INTENT_SECONDS = 5.0


@dataclass(frozen=True)
class Route:
    """The groups whose tools the model is shown, or ``out_of_scope``; ``source`` says how they were found ("rule",
    "previous", "model" or "fallback") for the eval and the log."""

    groups: tuple[str, ...]
    out_of_scope: bool = False
    source: str = "rule"
    # INC-03 K5-A: an out-of-scope route that asked for advice or a forecast gets its own fixed text.
    advice: bool = False
    # CHAT-09 K5-D: the one tool the words chose inside the groups, shown alone.
    only: str | None = None

    def tools(self) -> frozenset[str]:
        """The names of the tools the model is shown: the one the words chose, or the groups' tools."""
        if self.only is not None:
            return frozenset((self.only,))
        return frozenset(name for group in self.groups for name in GROUPS[group])


class IntentClassifier(Protocol):
    """Asks the model which group a message belongs to; returns one of the group names."""

    def classify(self, message: str, deadline: float) -> str: ...


def is_spending_question(message: str) -> bool:
    """True when the words of the message ask what was spent ("ne harcadım", "ne kadar verdim")."""
    return _SPENDING_WORDS.search(fold_turkish(message)) is not None


def group_of(tool: str) -> str | None:
    """The group a tool belongs to; None for a tool the table does not know."""
    return next((group for group, tools in GROUPS.items() if tool in tools), None)


# CHAT-08 K2-B: the five period tools take a range for the code that read the user's dates; the model never chooses dates (ANA-20).
DATE_FIELDS = ("from", "to")
CUSTOM_PERIOD = "Custom"


def hide_dates(tools: list[dict]) -> list[dict]:
    """The tools as the model is shown them: without the date range and without the ``Custom`` period; the input is not changed."""
    return [_without_dates(tool) for tool in tools]


def _without_dates(tool: dict) -> dict:
    function = tool["function"]
    properties = function.get("parameters", {}).get("properties", {})
    if not any(name in properties for name in DATE_FIELDS) and CUSTOM_PERIOD not in properties.get("period", {}).get("enum", []):
        return tool
    shown = {name: rule for name, rule in properties.items() if name not in DATE_FIELDS}
    if "period" in shown and "enum" in shown["period"]:
        shown["period"] = {**shown["period"], "enum": [p for p in shown["period"]["enum"] if p != CUSTOM_PERIOD]}
    return {**tool, "function": {**function, "parameters": {**function["parameters"], "properties": shown}}}


def narrow(tools: list[dict], route: Route) -> list[dict]:
    """The listed tools of the route's groups, in the server's order, plus every tool the table does not know; only the chosen
    one when the words chose one (K5-D)."""
    allowed = route.tools()
    if route.only is not None:
        return [t for t in tools if t["function"]["name"] in allowed]
    return [t for t in tools if t["function"]["name"] in allowed or group_of(t["function"]["name"]) is None]


def scenario_of(folded: str) -> str | None:
    """The what-if tool the words of a folded message name (K5-D); None when they name both, neither, or no what-if at all."""
    if _WHAT_IF.search(folded) is None:
        return None
    one_off, monthly = _ONE_OFF_WORDS.search(folded) is not None, _MONTHLY_WORDS.search(folded) is not None
    return ONE_OFF if one_off and not monthly else MONTHLY if monthly and not one_off else None


class Router:
    """Chooses the groups of one message (K1-C); ``classifier`` is only asked when no rule matches."""

    def __init__(self, classifier: IntentClassifier | None = None, clock=time.monotonic):
        self.classifier, self.clock = classifier, clock

    def route(self, message: str, previous_tool: str | None, deadline: float) -> Route:
        """The route of ``message``; ``previous_tool`` is the last call's tool when the message is a follow-up (CHAT-02)."""
        folded = fold_turkish(message)
        if _OUT_OF_SCOPE.search(folded):
            return Route((), out_of_scope=True)
        if _ADVICE_REQUEST.search(folded):
            return Route((), out_of_scope=True, advice=True)
        if _WEALTH_WORDS.search(folded):
            return Route((WEALTH,), only=scenario_of(folded))
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
