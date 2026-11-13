"""Questions offered under an answer and when the chat opens (CHAT-06).

The model never offers a question: a fixed table says which follow-ups come after which tool (K2-A, K5-A), and the starters
come from three yes/no checks of the customer's data, made through the MCP server without a model (K4-B). Decisions:
K3-C (an offered follow-up is remembered with its call; the same text in the next message runs that call without a tool
selection, anything else takes the normal path), K6-B (follow-ups under an explained answer only; three fixed questions under
an out-of-scope reply), K7-A (the reply carries the texts only).

CHAT-10 PR2: each tool has an ordered list of candidates; nothing already asked in the conversation is offered (K1-C, AskedStore),
the answer is looked at by counts only (K2-B, Shape), and every text is a full question that says its period (K3-B). A text with a
period is a pattern measured for two periods and run for every period by the modelless phrasing test (P1-B); a range is written out
by code (P2-A), a merchant's name takes no ending (P3-A), and with every candidate asked two general questions not asked yet are
offered (P4-A). A typed or late offered text therefore still reaches the right tool.
"""

import re
import time
from dataclasses import dataclass, field
from datetime import date

from answer_check import fold_turkish
from formatting import period as format_period
from memory import TokenMemory

MAX_SUGGESTIONS = 3
# P4-A: when every candidate was asked, at most this many general questions instead.
MAX_GENERAL = 2

# Starters and the out-of-scope reply (CHAT-06): questions every customer gets an answer to.
THIS_MONTH = "Bu ay ne kadar harcadım?"
RECURRING = "Aboneliklerim neler?"
UPCOMING_7 = "Önümüzdeki 7 günde hangi ödemelerim çıkacak?"
TOP_CATEGORY = "Geçen ay en çok hangi kategoriye harcadım?"
LAST_MONTH_TOTAL = "Geçen ay toplam ne kadar harcadım?"
OUT_OF_SCOPE_SUGGESTIONS = (THIS_MONTH, TOP_CATEGORY, LAST_MONTH_TOTAL)

# Full questions without a period, each a measured case (test_suggestions).
SAME_DAYS = "Bu ay, geçen ayın aynı günlerine göre daha mı çok harcıyorum?"
ROSE = "Geçen aya göre harcamam arttı mı?"
TREND_6 = "Son 6 ayda harcamam aylara göre nasıl değişti?"
SAVE_ADVISED = "Önerdiğin tutarı her ay biriktirseydim ne olurdu?"
OTHER_DURATION = "Peki {months} ay biriktirseydim?"

# CHAT-10 K3-B: the texts that say their period, as patterns. Each is measured for two periods and is a phrasing template for every
# period it is offered with (P1-B); the value is the periods it may be offered with.
CATEGORIES_IN = "{period} kategorilere göre harcamam nasıl?"
MERCHANTS_IN = "{period} harcamalarım işyerlerine göre nasıl dağıldı?"
HIGHEST_IN = "{period} en büyük harcamalarım neler?"
TOTAL_IN = "{period} toplam ne kadar harcadım?"
# P3-A: the merchant's name takes no ending, so no Turkish suffix is made up for a name the code does not know.
MERCHANT_IN = "{period} {merchant} harcamam ne kadar?"
INCOME_IN = "{period} maaşımın ne kadarını harcadım?"
SAVE_WHERE_IN = "{period} nerede tasarruf edebilirim?"

# The periods of the tools, as a sentence starts with them.
PERIOD_NAMES = {
    "Today": "Bugün", "Yesterday": "Dün", "ThisWeek": "Bu hafta", "LastWeek": "Geçen hafta", "Last7Days": "Son 7 günde",
    "Last30Days": "Son 30 günde", "ThisMonth": "Bu ay", "LastMonth": "Geçen ay", "Last3Months": "Son 3 ayda",
    "Last6Months": "Son 6 ayda", "ThisYear": "Bu yıl",
}
_ANY_PERIOD = (*PERIOD_NAMES, "Custom")
# Income and advice take a complete month only (INC-01 K2-A).
_COMPLETE_MONTH = ("LastMonth", "Custom")
PATTERNS = {CATEGORIES_IN: _ANY_PERIOD, MERCHANTS_IN: _ANY_PERIOD, HIGHEST_IN: _ANY_PERIOD, TOTAL_IN: _ANY_PERIOD,
            MERCHANT_IN: _ANY_PERIOD, INCOME_IN: _COMPLETE_MONTH, SAVE_WHERE_IN: _COMPLETE_MONTH}

# The period a follow-up turns to ("Peki geçen ay?" as a full question); a period with no counterpart turns to this month.
_OTHER_PERIOD = {"ThisMonth": "LastMonth", "LastMonth": "ThisMonth", "ThisWeek": "LastWeek", "LastWeek": "ThisWeek",
                 "Today": "Yesterday", "Yesterday": "Today"}

# Only a month has a month to compare with; the comparison of a day or a week is not a tool.
_COMPARISON_OF = {"ThisMonth": "MonthToDate", "LastMonth": "PreviousFullMonth"}
_COMPARISON_TEXT = {"MonthToDate": SAME_DAYS, "PreviousFullMonth": ROSE}
_PERIOD_OF = {"MonthToDate": "ThisMonth", "PreviousFullMonth": "LastMonth"}

# Kept as long as a conversation is likely to last (K1-C), and bounded like every memory of the service.
ASKED_SECONDS = 1800.0
MAX_ASKED = 50

# The starters are a nicety: a slow server shows the fixed examples on the phone instead of keeping the chat closed.
STARTER_SECONDS = 5.0


@dataclass(frozen=True)
class Suggestion:
    """A question to offer and the call it stands for."""

    text: str
    tool: str
    arguments: dict = field(default_factory=dict)


@dataclass(frozen=True)
class Shape:
    """What an answer holds, by counts only (K2-B): never a figure. ``None`` is "not known from this answer"."""

    spent: bool | None = None
    transactions: int | None = None
    payments: int | None = None


def _s(text: str, tool: str, **arguments) -> Suggestion:
    return Suggestion(text, tool, arguments)


def _in(pattern: str, tool: str, period: dict, **arguments) -> Suggestion:
    """A pattern written for one period: a name ("Bu ay"), or a range written out by code (P2-A)."""
    if period["period"] == "Custom":
        name = f"{format_period(date.fromisoformat(period['from']), date.fromisoformat(period['to']))} döneminde"
    else:
        name = PERIOD_NAMES[period["period"]]
    text = pattern.format(period=name, merchant=arguments.get("merchant", ""))
    return Suggestion(text, tool, {**arguments, **period})


def _period(arguments: dict) -> dict:
    """The period of a call with its days when it is a range, so a follow-up about the same period runs the same days."""
    return {key: arguments[key] for key in ("period", "from", "to") if key in arguments}


def _other(arguments: dict) -> dict:
    return {"period": _OTHER_PERIOD.get(arguments["period"], "ThisMonth")}


def _named(name: str) -> dict:
    return {"period": name}


def _summary(a: dict) -> list[Suggestion]:
    p = _period(a)
    rows = [_in(CATEGORIES_IN, "get_category_breakdown", p), _in(MERCHANTS_IN, "get_merchant_breakdown", p)]
    if a["period"] in _COMPARISON_OF:
        comparison = _COMPARISON_OF[a["period"]]
        rows.append(_s(_COMPARISON_TEXT[comparison], "compare_spending", comparison=comparison))
    rows.append(_in(HIGHEST_IN, "get_highest_transactions", p))
    return rows + [_in(TOTAL_IN, "get_spending_summary", _other(a))]


def _categories(a: dict) -> list[Suggestion]:
    p = _period(a)
    return [_in(MERCHANTS_IN, "get_merchant_breakdown", p), _in(HIGHEST_IN, "get_highest_transactions", p),
            _in(CATEGORIES_IN, "get_category_breakdown", _other(a))]


def _merchants(a: dict) -> list[Suggestion]:
    p = _period(a)
    return [_in(CATEGORIES_IN, "get_category_breakdown", p), _in(HIGHEST_IN, "get_highest_transactions", p),
            _in(MERCHANTS_IN, "get_merchant_breakdown", _other(a))]


def _highest(a: dict) -> list[Suggestion]:
    p = _period(a)
    return [_in(CATEGORIES_IN, "get_category_breakdown", p), _in(MERCHANTS_IN, "get_merchant_breakdown", p),
            _in(HIGHEST_IN, "get_highest_transactions", _other(a))]


def _merchant(a: dict) -> list[Suggestion]:
    m = a["merchant"]
    return [_in(MERCHANT_IN, "get_merchant_spending", _named("Last3Months"), merchant=m),
            _in(MERCHANTS_IN, "get_merchant_breakdown", _period(a)),
            _in(MERCHANT_IN, "get_merchant_spending", _other(a), merchant=m)]


def _comparison(a: dict) -> list[Suggestion]:
    p = _named(_PERIOD_OF[a["comparison"]])
    return [_in(CATEGORIES_IN, "get_category_breakdown", p), _s(TREND_6, "get_monthly_trend", months=6),
            _in(HIGHEST_IN, "get_highest_transactions", p)]


def _income(a: dict) -> list[Suggestion]:
    p = _period(a)
    return [_in(SAVE_WHERE_IN, "get_spending_advice", p), _in(CATEGORIES_IN, "get_category_breakdown", p)]


def _advice(a: dict) -> list[Suggestion]:
    # The advised saving of the what-if is always last month's (INC-03 K9-A): offered under another month's advice it would answer
    # a different month than the one on the screen.
    if a["period"] != "LastMonth":
        return []
    last = _named("LastMonth")
    return [_s(SAVE_ADVISED, "what_if_monthly", fromAdvice=True), _in(INCOME_IN, "get_income_share", last),
            _in(CATEGORIES_IN, "get_category_breakdown", last)]


def _saving(a: dict) -> list[Suggestion]:
    # INV-03 K8-A, D4-A: two other durations of the question's examples, longest first; the amount (or the advice) is kept. A
    # follow-up text, so code runs it when tapped or typed (D1-B), never the model.
    others = [n for n in (12, 6, 3) if n != a["months"]][:2]
    return [_s(OTHER_DURATION.format(months=n), "what_if_monthly", **{**a, "months": n}) for n in others]


# The candidates of each tool, best first (K1-C); the first three not asked and fitting the answer are offered.
_TABLE = {
    "get_spending_summary": _summary,
    "get_category_breakdown": _categories,
    "get_merchant_spending": _merchant,
    "get_merchant_breakdown": _merchants,
    "get_highest_transactions": _highest,
    "compare_spending": _comparison,
    "get_monthly_trend": lambda a: [_s(ROSE, "compare_spending", comparison="PreviousFullMonth"),
                                    _in(CATEGORIES_IN, "get_category_breakdown", _named("LastMonth")),
                                    _in(TOTAL_IN, "get_spending_summary", _named("ThisMonth"))],
    "get_recurring_payments": lambda a: [_s(UPCOMING_7, "get_upcoming_payments", days=7), _s(TREND_6, "get_monthly_trend", months=6)],
    "get_upcoming_payments": lambda a: [_s(RECURRING, "get_recurring_payments"),
                                        _in(TOTAL_IN, "get_spending_summary", _named("ThisMonth"))],
    # A budget is about the running month only (ANA-13).
    "check_budget": lambda a: [_in(MERCHANTS_IN, "get_merchant_breakdown", _named("ThisMonth")),
                               _in(CATEGORIES_IN, "get_category_breakdown", _named("ThisMonth")),
                               _in(HIGHEST_IN, "get_highest_transactions", _named("ThisMonth"))],
    # INC-03 K19-B: income share → advice → advised saving; the one-off offers nothing (no "buy more" chain), the monthly saving
    # only other durations of the same amount (INV-03 K8-A).
    "get_income_share": _income,
    "get_spending_advice": _advice,
    "what_if_monthly": _saving,
    # No other cut is offered: a figure the user did not say would be the backend's suggestion (MCP-17/18).
    "simulate_savings": lambda a: [_s(TREND_6, "get_monthly_trend", months=6),
                                   _in(CATEGORIES_IN, "get_category_breakdown", _named("LastMonth"))],
}

# P4-A: offered when every candidate was asked; never under an answer that offers nothing on purpose.
_GENERAL = (_s(THIS_MONTH, "get_spending_summary", period="ThisMonth"), _s(ROSE, "compare_spending", comparison="PreviousFullMonth"),
            _s(RECURRING, "get_recurring_payments"), _s(UPCOMING_7, "get_upcoming_payments", days=7),
            _s(TREND_6, "get_monthly_trend", months=6))

_BREAKDOWNS = ("get_category_breakdown", "get_merchant_breakdown", "get_highest_transactions")


def follow_ups(tool: str, arguments: dict, asked=(), shape: Shape | None = None) -> list[Suggestion]:
    """The follow-ups to offer after ``tool`` ran with ``arguments``: its candidates without the call itself, anything ``asked`` in
    the conversation and what the answer's ``shape`` makes pointless; at most three."""
    build = _TABLE.get(tool)
    if build is None:
        return []
    try:
        rows = build(arguments)
    except KeyError:
        # An argument the table needs is missing (a tool whose schema changed on the server): nothing is offered.
        return []
    if not rows:
        return []
    seen = [(tool, arguments), *asked]
    kept = [r for r in rows if (r.tool, r.arguments) not in seen and _fits(r, arguments, shape or Shape())]
    if not kept and all((r.tool, r.arguments) in seen for r in rows):
        # Every candidate was asked: the conversation goes on with what it has not touched yet (P4-A).
        kept = [g for g in _GENERAL if (g.tool, g.arguments) not in seen][:MAX_GENERAL]
    return [Suggestion(r.text, r.tool, dict(r.arguments)) for r in kept[:MAX_SUGGESTIONS]]


def _fits(row: Suggestion, arguments: dict, shape: Shape) -> bool:
    """False for a follow-up the answer already makes pointless (K2-B)."""
    same_period = "period" in arguments and _period(row.arguments) == _period(arguments)
    if shape.spent is False:
        # Nothing was spent: only another period can show something.
        return "period" in row.arguments and not same_period
    if shape.transactions == 1 and same_period and row.tool in _BREAKDOWNS:
        # One purchase has one category, one merchant and is itself the biggest one.
        return False
    if shape.payments == 0 and row.tool == "get_upcoming_payments":
        return False
    return True


def result_shape(tool: str, result: dict) -> Shape:
    """The counts of a result that the follow-ups look at; a result the code cannot read is an unknown shape, never an error."""
    try:
        if tool in ("get_recurring_payments", "get_upcoming_payments"):
            return Shape(payments=len(result["payments"]))
        if tool == "get_spending_summary":
            rows = result["currencies"]
            return Shape(spent=any(r["totalAmount"] > 0 for r in rows), transactions=sum(r["transactionCount"] for r in rows))
        if tool == "get_highest_transactions":
            return Shape(spent=any(r["items"] for r in result["currencies"]))
        if tool in ("get_category_breakdown", "get_merchant_breakdown", "get_merchant_spending"):
            return Shape(spent=bool(result["currencies"]))
    except (KeyError, TypeError):
        pass
    return Shape()


class AskedStore:
    """The calls each user asked in the conversation (K1-C): kept per token, forgotten ``ttl_seconds`` after the last one."""

    def __init__(self, ttl_seconds: float = ASKED_SECONDS, capacity: int = 1000, clock=time.monotonic):
        self._memory = TokenMemory(ttl_seconds, capacity, clock)

    def remember(self, token: str, tool: str, arguments: dict) -> None:
        """Adds a call that was answered; the same call twice is kept once, the oldest goes past ``MAX_ASKED``."""
        call = (tool, dict(arguments))
        kept = [c for c in (self._memory.get(token) or []) if c != call]
        self._memory.put(token, [*kept, call][-MAX_ASKED:])

    def recall(self, token: str) -> list[tuple[str, dict]]:
        """The calls asked, oldest first; an empty list when none were or the conversation went quiet too long."""
        return list(self._memory.get(token) or [])


def _plain(text: str) -> str:
    return " ".join(re.sub(r"[?.!\s]+$", "", fold_turkish(text).strip()).split())


def match(offered: list[Suggestion], message: str) -> Suggestion | None:
    """The offered suggestion the message is, ignoring case, spacing and the closing mark; None for any other text."""
    plain = _plain(message)
    return next((suggestion for suggestion in offered if _plain(suggestion.text) == plain), None) if plain else None


class OfferedStore:
    """The follow-ups offered to each user, for the next message only (memory.TokenMemory, like the waiting question)."""

    def __init__(self, ttl_seconds: float = 600.0, capacity: int = 1000, clock=time.monotonic):
        self._memory = TokenMemory(ttl_seconds, capacity, clock)

    def put(self, token: str, suggestions: list[Suggestion]) -> None:
        """Keeps what was offered, replacing an older offer."""
        self._memory.put(token, list(suggestions))

    def take(self, token: str) -> list[Suggestion]:
        """What was offered last, removed; an empty list when nothing was or it is too old."""
        return self._memory.pop(token) or []

    def put_back(self, token: str, suggestions: list[Suggestion]) -> None:
        """Gives back an offer whose follow-up failed to be answered (CHAT-10 K10-A), unless the turn already made a new one."""
        if suggestions and self._memory.get(token) is None:
            self._memory.put(token, list(suggestions))


class StarterQuestions:
    """The questions shown when the chat opens: the first three whose condition holds for the customer (K4-B, K5-A)."""

    def __init__(self, gateway, clock=time.monotonic):
        self.gateway, self.clock = gateway, clock

    def for_token(self, token: str) -> list[str]:
        """The starters for the user whose token is given; only whether each list is empty is looked at, never a figure."""
        deadline = self.clock() + STARTER_SECONDS
        due_soon = bool(self.gateway.call("get_upcoming_payments", {"days": 7}, token, deadline)["payments"])
        spent = bool(self.gateway.call("get_spending_summary", {"period": "ThisMonth"}, token, deadline)["currencies"])
        recurring = bool(self.gateway.call("get_recurring_payments", {}, token, deadline)["payments"])
        # Most pressing first: a payment within a week, this month, the subscriptions; then two that always have an answer.
        rules = ((due_soon, UPCOMING_7), (spent, THIS_MONTH), (recurring, RECURRING), (True, TOP_CATEGORY),
                 (True, LAST_MONTH_TOTAL))
        return [text for holds, text in rules if holds][:MAX_SUGGESTIONS]

