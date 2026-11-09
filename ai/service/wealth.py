"""What the chat checks in code before the income, advice and "what if" tools run (INC-03 PR2).

The model chooses the tool; these rules decide whether its arguments are the user's own. The monthly saving is built only from
what the message says (K8-A, K17-B) and the advised amount only when the user asked for it (K9-A); the two complete-month analyses
run only for a whole, finished calendar month (K15-A). Standard library plus the grounding helpers, deterministic.
"""

import calendar
import re
from dataclasses import dataclass, field
from datetime import date

from answer_check import fold_rule, fold_turkish
from conversation import LastCall
from grounding import amount_currency_in, amounts_in, category_in, json_number, mentions_amount, mentions_period, period_in

# The analyses that take a complete month (the server publishes LastMonth and Custom only).
MONTH_TOOLS = ("get_income_share", "get_spending_advice")
MONTHLY_TOOL = "what_if_monthly"
# The tools whose answers carry a figure that is a past price or a rule's advice: a text that sounds like advice is never shown.
WEALTH_TOOLS = (*MONTH_TOOLS, "what_if_one_off", MONTHLY_TOOL)

LIRA = "TRY"
LAST_MONTH = "LastMonth"

# "Önerdiğin tutarı", "önerilen tutar", "o tutarı": the user points at the advice's monthly saving.
_ADVISED_AMOUNT = re.compile(fold_rule(r"\böner|\bo\s+tutar"))


@dataclass(frozen=True)
class MonthlyPlan:
    """What to do with a monthly-saving call: ``run`` the arguments, ``ask`` for the figure named (``known`` stays, ``later`` is
    asked after it), or answer that only lira is supported."""

    run: dict | None = None
    ask: str | None = None
    known: dict = field(default_factory=dict)
    later: tuple[str, ...] = ()
    lira_only: bool = False


def plan_monthly(arguments: dict, message: str, after_advice: bool) -> MonthlyPlan:
    """The monthly saving call as the user's words allow it (K8-A, K9-A, K17-B); the model's own figures are never trusted."""
    amount = arguments.get("amount")
    said = amount if mentions_amount(message, amount) else None
    if said is None:
        advised = arguments.get("fromAdvice") is True and (after_advice or _ADVISED_AMOUNT.search(fold_turkish(message)) is not None)
        if advised:
            return MonthlyPlan(run={"fromAdvice": True})
        # No amount in the message: only the amount is asked (CHAT-09 K6-B).
        return MonthlyPlan(ask="amount", known={})
    # CHAT-09 K6-B: only lira can run, so an amount with no currency beside it is lira (the answer's "Aylık tutar" line shows it);
    # another currency beside it is refused, never converted (INC-03 K8-A).
    if amount_currency_in(message) not in (None, LIRA):
        return MonthlyPlan(lira_only=True)
    return MonthlyPlan(run={"amount": amount, "currencyCode": LIRA})


def chosen_scenario_arguments(tool: str, message: str) -> dict | None:
    """The arguments of a what-if the words chose but the model did not (CHAT-09 K13-A), read from the message as the checks would
    accept them: a one-off's period and category, a monthly saving's single amount (asked when there is none or several).
    None when a one-off's period must be asked."""
    if tool == MONTHLY_TOOL:
        amounts = [amount for amount in amounts_in(message) if amount > 0]
        return {"amount": json_number(amounts[0])} if len(amounts) == 1 else {}
    period = period_in(message)
    if period is None:
        return None
    category = category_in(message)
    return {"period": period, **({"categoryCode": category} if category else {})}


def advised_saving_call(message: str, previous: LastCall | None) -> tuple[str, dict] | None:
    """The call for "had I saved the amount you advised" when the words say so (K9-A); None for any other message.

    Code chooses this one: the small model never picked it by itself (INC-03 PR2 measurement, W12/W13), and its meaning is only in
    the words ("önerdiğin tutarı biriktirseydim"). "Bu tutarı" counts only right after an advice answer.
    """
    folded = fold_turkish(message)
    if re.search(r"biriktir", folded) is None or re.search(r"(?:sa|se)ydi(?:m|k)\b", folded) is None:
        return None
    after_advice = previous is not None and previous.tool == "get_spending_advice"
    if re.search(fold_rule(r"\böner"), folded) or (after_advice and re.search(r"\b(?:bu|o)\s+tutar", folded)):
        return MONTHLY_TOOL, {"fromAdvice": True}
    return None


def is_complete_month(start: date, end: date, today: date) -> bool:
    """True for a whole calendar month that is over (K15-A): the first to the last day, the last day before today."""
    return start.day == 1 and end == date(start.year, start.month, calendar.monthrange(start.year, start.month)[1]) and end < today


def needs_month_question(message: str, arguments: dict, previous: LastCall | None) -> bool:
    """True when the user did not ask for last month: the model can only write LastMonth, so a "bu ay" or "son 3 ay" would be
    answered with last month's figures without the user knowing."""
    named = period_in(message)
    if named == LAST_MONTH:
        return False
    if named is not None or mentions_period(message):
        return True
    # A follow-up that says no period keeps the last call's, as everywhere else (CHAT-02).
    return not (previous is not None and previous.arguments.get("period") == arguments.get("period"))


def has_complete_month(arguments: dict, today: date) -> bool:
    """True when a call of a month tool asks for last month or for one whole, finished calendar month (K15-A)."""
    if arguments.get("period") == LAST_MONTH:
        return True
    if arguments.get("period") != "Custom":
        return False
    try:
        return is_complete_month(date.fromisoformat(arguments["from"]), date.fromisoformat(arguments["to"]), today)
    except (KeyError, TypeError, ValueError):
        return False
