"""Questions offered under an answer and when the chat opens (CHAT-06).

The model never offers a question: a fixed table says which follow-ups come after which tool (K2-A, K5-A), and the starters
come from three yes/no checks of the customer's data, made through the MCP server without a model (K4-B). Decisions:
K3-C (an offered follow-up is remembered with its call; the same text in the next message runs that call without a tool
selection, anything else takes the normal path), K6-B (follow-ups under an explained answer only; three fixed questions under
an out-of-scope reply), K7-A (the reply carries the texts only).

The short texts are the eval's follow-up cases (F03, F04, F05, F07, F11); a full question offered is one of its measured cases
(test_suggestions checks both), so a typed or late one still reaches the right tool.
"""

import re
import time
from dataclasses import dataclass, field

from answer_check import fold_turkish
from memory import TokenMemory

MAX_SUGGESTIONS = 3

CATEGORIES = "Kategorilere göre?"
MERCHANTS = "Peki işyerlerine göre?"
COMPARE = "Bir önceki ayla karşılaştır"
HIGHEST = "En büyük harcamalarım neler?"
LAST_MONTH = "Peki geçen ay?"
LAST_3_MONTHS = "Ya son 3 ayda?"
SIX_MONTHS = "6 aylık göster"
UPCOMING_7 = "Önümüzdeki 7 günde hangi ödemelerim çıkacak?"
THIS_MONTH = "Bu ay ne kadar harcadım?"
RECURRING = "Aboneliklerim neler?"
TOP_CATEGORY = "Geçen ay en çok hangi kategoriye harcadım?"
LAST_MONTH_TOTAL = "Geçen ay toplam ne kadar harcadım?"
TOP_MERCHANT_THIS_MONTH = "Bu ay en çok hangi işyerine harcadım?"
ROSE = "Geçen aya göre harcamam arttı mı?"
TREND_6 = "Son 6 ayda harcamam aylara göre nasıl değişti?"

# Questions every customer gets an answer to, even one with no spending yet ("harcama bulunamadı" is an answer).
OUT_OF_SCOPE_SUGGESTIONS = (THIS_MONTH, TOP_CATEGORY, LAST_MONTH_TOTAL)

# Only a month has a month to compare with; the comparison of a day or a week is not a tool.
_COMPARISON_OF = {"ThisMonth": "MonthToDate", "LastMonth": "PreviousFullMonth"}
_PERIOD_OF = {"MonthToDate": "ThisMonth", "PreviousFullMonth": "LastMonth"}

# The starters are a nicety: a slow server shows the fixed examples on the phone instead of keeping the chat closed.
STARTER_SECONDS = 5.0


@dataclass(frozen=True)
class Suggestion:
    """A question to offer and the call it stands for."""

    text: str
    tool: str
    arguments: dict = field(default_factory=dict)


def _s(text: str, tool: str, **arguments) -> Suggestion:
    return Suggestion(text, tool, arguments)


def _summary(a: dict) -> list[Suggestion]:
    p = a["period"]
    last = (_s(COMPARE, "compare_spending", comparison=_COMPARISON_OF[p]) if p in _COMPARISON_OF
            else _s(HIGHEST, "get_highest_transactions", period=p))
    return [_s(CATEGORIES, "get_category_breakdown", period=p), _s(MERCHANTS, "get_merchant_breakdown", period=p), last]


def _categories(a: dict) -> list[Suggestion]:
    p = a["period"]
    rows = [_s(MERCHANTS, "get_merchant_breakdown", period=p), _s(HIGHEST, "get_highest_transactions", period=p)]
    return rows + [_s(LAST_MONTH, "get_category_breakdown", period="LastMonth")] if p == "ThisMonth" else rows


def _merchant(a: dict) -> list[Suggestion]:
    return [_s(LAST_3_MONTHS, "get_merchant_spending", merchant=a["merchant"], period="Last3Months"),
            _s(MERCHANTS, "get_merchant_breakdown", period=a["period"])]


def _merchants(a: dict) -> list[Suggestion]:
    return [_s(CATEGORIES, "get_category_breakdown", period=a["period"]),
            _s(HIGHEST, "get_highest_transactions", period=a["period"])]


def _highest(a: dict) -> list[Suggestion]:
    return [_s(CATEGORIES, "get_category_breakdown", period=a["period"]),
            _s(MERCHANTS, "get_merchant_breakdown", period=a["period"])]


def _comparison(a: dict) -> list[Suggestion]:
    return [_s(CATEGORIES, "get_category_breakdown", period=_PERIOD_OF[a["comparison"]]),
            _s(SIX_MONTHS, "get_monthly_trend", months=6)]


_TABLE = {
    "get_spending_summary": _summary,
    "get_category_breakdown": _categories,
    "get_merchant_spending": _merchant,
    "get_merchant_breakdown": _merchants,
    "get_highest_transactions": _highest,
    "compare_spending": _comparison,
    "get_monthly_trend": lambda a: [_s(ROSE, "compare_spending", comparison="PreviousFullMonth"),
                                    _s(TOP_CATEGORY, "get_category_breakdown", period="LastMonth")],
    "get_recurring_payments": lambda a: [_s(UPCOMING_7, "get_upcoming_payments", days=7)],
    "get_upcoming_payments": lambda a: [_s(RECURRING, "get_recurring_payments")],
    # A budget is about the running month only (ANA-13).
    "check_budget": lambda a: [_s(TOP_MERCHANT_THIS_MONTH, "get_merchant_breakdown", period="ThisMonth"),
                               _s(CATEGORIES, "get_category_breakdown", period="ThisMonth")],
    # No other cut is offered: a figure the user did not say would be the backend's suggestion (MCP-17/18).
    "simulate_savings": lambda a: [_s(TREND_6, "get_monthly_trend", months=6),
                                   _s(TOP_CATEGORY, "get_category_breakdown", period="LastMonth")],
}


def follow_ups(tool: str, arguments: dict) -> list[Suggestion]:
    """The follow-ups to offer after ``tool`` ran with ``arguments``; never the very same call, at most three."""
    build = _TABLE.get(tool)
    if build is None:
        return []
    try:
        rows = build(arguments)
        if arguments.get("period") == "Custom":
            # CHAT-08 K7-A: an offered question about the same days runs over the same days.
            rows = [_over_range(row, arguments) for row in rows]
    except KeyError:
        # An argument the table needs is missing (a tool whose schema changed on the server): nothing is offered.
        return []
    return [r for r in rows if (r.tool, r.arguments) != (tool, arguments)][:MAX_SUGGESTIONS]


def _over_range(row: Suggestion, arguments: dict) -> Suggestion:
    if row.arguments.get("period") != "Custom":
        return row
    return Suggestion(row.text, row.tool, {**row.arguments, "from": arguments["from"], "to": arguments["to"]})


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
