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

from accounts import SEARCH, tool_of as account_tool
from answer_check import fold_rule, fold_turkish
from ollama_client import OllamaError

GENERAL, PLANNING, WEALTH, ACCOUNTS = "general", "planning", "wealth", "accounts"

# A tool the table does not know is shown in every group (narrow): a tool added on the server must not vanish.
GROUPS = {
    GENERAL: ("get_spending_summary", "get_category_breakdown", "get_merchant_spending", "get_merchant_breakdown",
              "get_highest_transactions", "compare_spending", "get_monthly_trend", "get_recurring_payments",
              "get_upcoming_payments",
              # UX-01 PR3 K6-B: the category detail is a spending question too; its description, not a rule, tells it apart.
              "get_category_spending"),
    PLANNING: ("check_budget", "simulate_savings"),
    # INC-03 K3-A: income, advice and the what-if scenarios; reached by word rules only (K16-A: the model never picks this group).
    WEALTH: ("get_income_share", "get_spending_advice", "what_if_one_off", "what_if_monthly"),
    # ACC-01a K5-A: balances, the transaction search, income and transfers; reached by word rules (ACC-01b K2-C, accounts.tool_of)
    # and by a follow-up of one of them, never by the model's intent question.
    ACCOUNTS: ("get_accounts", "search_transactions", "get_income", "get_transfers"),
}

# The model's answer (intent-v1 names three kinds of question); spending and payments share the general group.
_MODEL_GROUPS = {"spending": GENERAL, "payments": GENERAL, "planning": PLANNING}

# K5-B: only requests that are clearly not about the user's spending; a question about transfers is not one of them.
# ACC-01b: only a request; "gönderdim", "transfer yaptım", "havale ettim" (t/d + i after the stem) ask what was sent, a get_transfers
# question that this rule used to answer with the out-of-scope text.
_NOT_PAST = r"(?![td]i)"
_OUT_OF_SCOPE = re.compile(fold_rule(
    rf"\btransfer\s+(?:yap|et|gönder){_NOT_PAST}|\bpara\s+gönder{_NOT_PAST}|\bhavale\s+(?:yap|et){_NOT_PAST}"
    rf"|\beft\s+(?:yap|et){_NOT_PAST}"
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
# CHAT-09 PR3: "son 3 ayda", "bir ayda" are a period, not "per month"; found when a one-off scenario test reached the monthly tool.
_COUNTED_MONTHS = re.compile(fold_rule(r"\b(?:\d+|bir|iki|üç|dört|beş|altı|yedi|sekiz|dokuz|on|on\s+iki)\s+ayda\b"))
ONE_OFF, MONTHLY = "what_if_one_off", "what_if_monthly"

# A planning word decides even when spending words are there too ("Starbucks harcamamı yarıya indirsem" is a savings question).
_PLANNING_WORDS = re.compile(fold_rule(
    r"bütçe|\blimit|\bayir(?:dim|acağim|sam)|tasarruf|biriktir|\bazalt|\bkis(?:sa|ar|al)|yariya|\bindirs|\bindirir|çeyre"
    # CHAT-09 K14-A: "sınır" folds to "sinir" (nerve), so only a limit's own shapes: set, or followed by a figure or "aş…".
    r"|\bsınır\s+(?:koy|belirle)|\bsınır(?:ı|ım|ımı)?\s+(?:\d|aş)"))
# CHAT-09 K19-C: the planning tool the words name, used only when the model chose nothing from the planning list. "Aş…" (exceed)
# and a limit are a budget's; cutting or putting aside is a saving's. "Kıs" folds like "kis", so its verb shapes only.
_BUDGET_WORDS = re.compile(fold_rule(
    r"bütçe|\blimit|\bsınır\s+(?:koy|belirle)|\bsınır(?:ı|ım|ımı)?\s+(?:\d|aş)|\baş(?:ar|acak|tı|ıyor|mam|ar\s+mı)"))
_SAVING_WORDS = re.compile(fold_rule(
    r"tasarruf|biriktir|\bazalt|\bkıs(?:sa|ar|al|ıp|mak)|yarıya|\bindirs|\bindirir|çeyre"))
BUDGET, SAVING = "check_budget", "simulate_savings"

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
    # CHAT-09 K17-C: a what-if whose words name both scenarios or neither ("both", "neither"); the chat asks which one.
    undecided: str | None = None

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
    """The tools as the model is shown them: without the date range and without the ``Custom`` period, and without the monthly
    saving's duration (INV-03 D2-A); code reads both from the words. The input is not changed."""
    return [_without_duration(_without_dates(tool)) for tool in tools]


def _without_duration(tool: dict) -> dict:
    # INV-03 D2-A: "6 ay" is also the monthly trend's count; a field the model does not see is one it cannot fill with a guess, and
    # the list stays the one the selection was measured with.
    function = tool["function"]
    parameters = function.get("parameters", {})
    if function["name"] != MONTHLY or "months" not in parameters.get("properties", {}):
        return tool
    shown = {name: rule for name, rule in parameters["properties"].items() if name != "months"}
    required = [name for name in parameters.get("required", []) if name != "months"]
    return {**tool, "function": {**function, "parameters": {**parameters, "properties": shown, "required": required}}}


def _without_dates(tool: dict) -> dict:
    function = tool["function"]
    properties = function.get("parameters", {}).get("properties", {})
    if not any(name in properties for name in DATE_FIELDS) and CUSTOM_PERIOD not in properties.get("period", {}).get("enum", []):
        return tool
    shown = {name: rule for name, rule in properties.items() if name not in DATE_FIELDS}
    if "period" in shown and "enum" in shown["period"]:
        shown["period"] = {**shown["period"], "enum": [p for p in shown["period"]["enum"] if p != CUSTOM_PERIOD]}
    return {**tool, "function": {**function, "parameters": {**function["parameters"], "properties": shown}}}


# ACC-01b finding: a fixed order, never the server's, which changes from build to build (the same code and tools passed S33 and F01
# 6/6 in one order and 0/6 in another; the table's own order broke S33 and Q07 instead). This is the order every measurement up to
# 16 Nov 2026 ran with (the server's then, and mcp_tools.json's), then the tools added since; a tool not here comes last.
MEASURED_ORDER = (
    "get_recurring_payments", "simulate_savings", "check_budget", "what_if_monthly", "get_highest_transactions",
    "get_upcoming_payments", "get_merchant_spending", "what_if_one_off", "get_category_spending", "compare_spending",
    "get_spending_summary", "get_category_breakdown", "get_monthly_trend", "get_income_share", "get_spending_advice",
    "get_merchant_breakdown", *GROUPS[ACCOUNTS])
_ORDER = {name: index for index, name in enumerate(MEASURED_ORDER)}


def narrow(tools: list[dict], route: Route) -> list[dict]:
    """The listed tools of the route's groups, in the measured order, plus every tool the table does not know (after them); only
    the chosen one when the words chose one (K5-D)."""
    allowed = route.tools()
    if route.only is not None:
        kept = [t for t in tools if t["function"]["name"] in allowed]
    else:
        kept = [t for t in tools if t["function"]["name"] in allowed or group_of(t["function"]["name"]) is None]
    return sorted(kept, key=lambda t: _ORDER.get(t["function"]["name"], len(_ORDER)))


def scenario_of(folded: str) -> str | None:
    """The what-if tool the words of a folded message name (K5-D); None when they name both, neither, or no what-if at all."""
    words = _scenario_words(folded)
    return words if words in (ONE_OFF, MONTHLY) else None


def _scenario_words(folded: str) -> str | None:
    """The scenario the words name, "both" or "neither"; None when the message is no what-if."""
    if _WHAT_IF.search(folded) is None:
        return None
    one_off = _ONE_OFF_WORDS.search(folded) is not None
    monthly = _MONTHLY_WORDS.search(_COUNTED_MONTHS.sub(" ", folded)) is not None
    return ("both" if monthly else ONE_OFF) if one_off else MONTHLY if monthly else "neither"


def planning_of(message: str) -> str | None:
    """The planning tool the words of a message name (K19-C); None when they name both or neither."""
    folded = fold_turkish(message)
    budget, saving = _BUDGET_WORDS.search(folded) is not None, _SAVING_WORDS.search(folded) is not None
    return BUDGET if budget and not saving else SAVING if saving and not budget else None


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
        # ACC-01b K2-C: a balance, income or transfer question before the wealth words, which "maaş" and "gelir" are too; the
        # income share keeps its own ("maaşımın ne kadarını harcadım", accounts._NOT_INCOME).
        account = account_tool(folded)
        if account is not None and account != SEARCH:
            return Route((ACCOUNTS,), only=account)
        if _WEALTH_WORDS.search(folded):
            words = _scenario_words(folded)
            if words in ("both", "neither"):
                return Route((WEALTH,), undecided=words)
            return Route((WEALTH,), only=words)
        if _PLANNING_WORDS.search(folded):
            return Route((PLANNING,))
        # A limit is a search only after the planning words: "1.000 TL'den fazla azaltsam" is a saving.
        if account == SEARCH:
            return Route((ACCOUNTS,), only=SEARCH)
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
