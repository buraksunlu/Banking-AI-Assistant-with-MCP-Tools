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
from clarification import DURATION_LIMITS, MAXIMUM_MONTHS, MINIMUM_MONTHS
from conversation import LastCall, opens_follow_up
from grounding import (amount_currency_in, amounts_in, category_in, json_number, mentions_amount, mentions_period, months_in,
                       period_in, periods_in, without_durations)

# The analyses that take a complete month (the server publishes LastMonth and Custom only).
MONTH_TOOLS = ("get_income_share", "get_spending_advice")
MONTHLY_TOOL = "what_if_monthly"
ONE_OFF_TOOL = "what_if_one_off"
# The tools whose answers carry a figure that is a past price or a rule's advice: a text that sounds like advice is never shown.
WEALTH_TOOLS = (*MONTH_TOOLS, ONE_OFF_TOOL, MONTHLY_TOOL)

LIRA = "TRY"
LAST_MONTH = "LastMonth"

# "Önerdiğin tutarı", "önerilen tutar", "o tutarı": the user points at the advice's monthly saving.
_ADVISED_AMOUNT = re.compile(fold_rule(r"\böner|\bo\s+tutar"))


@dataclass(frozen=True)
class MonthlyPlan:
    """What to do with a monthly-saving call: ``run`` the arguments, ``ask`` for the figure named (``known`` stays, ``later`` is
    asked after it, ``note`` says why a said one was refused), or answer that only lira is supported."""

    run: dict | None = None
    ask: str | None = None
    known: dict = field(default_factory=dict)
    later: tuple[str, ...] = ()
    lira_only: bool = False
    note: str = ""


def plan_monthly(arguments: dict, message: str, after_advice: bool, carried: dict | None = None) -> MonthlyPlan:
    """The monthly saving call as the user's words allow it (K8-A, K9-A, K17-B); the model's own figures are never trusted.

    INV-03 PR2: the duration is the user's too (K2-A, K5-A). ``carried`` is the last monthly saving's arguments in a "peki/ya"
    follow-up (K7-A): what the message says replaces it, the rest is kept.
    """
    carried = carried or {}
    source = _amount_source(arguments, message, after_advice, carried)
    if source == "lira_only":
        return MonthlyPlan(lira_only=True)
    durations = months_in(message)
    months = durations[0] if len(durations) == 1 else None if durations else carried.get("months")
    known = {**(source or {}), **({"months": months} if months is not None and _in_limits(months) else {})}
    if months is not None and not _in_limits(months):
        # K6-A: a duration outside the limits is asked first, with the limits; the amount, if missing, after it.
        return MonthlyPlan(ask="months", known=known, later=() if source else ("amount",), note=DURATION_LIMITS)
    if source is None:
        # No amount in the message: the amount is asked (CHAT-09 K6-B), then the duration if it is missing too.
        return MonthlyPlan(ask="amount", known=known, later=() if "months" in known else ("months",))
    if "months" not in known:
        # K2-A, and D5-A: two durations in one message are asked, not chosen between.
        return MonthlyPlan(ask="months", known=known)
    return MonthlyPlan(run=known)


def _in_limits(months: int) -> bool:
    return MINIMUM_MONTHS <= months <= MAXIMUM_MONTHS


def _amount_source(arguments: dict, message: str, after_advice: bool, carried: dict) -> dict | str | None:
    """The amount arguments the words allow, "lira_only" for another currency, None when there is no amount to run."""
    amount = arguments.get("amount")
    said = amount if mentions_amount(message, amount) else None
    if said is None and carried:
        # D1-B: code, not the model, built a follow-up's call; its amount is read from the words, as K13-A reads them.
        amounts = [a for a in amounts_in(message) if a > 0]
        said = json_number(amounts[0]) if len(amounts) == 1 else None
    if said is None:
        advised = arguments.get("fromAdvice") is True and (after_advice or _ADVISED_AMOUNT.search(fold_turkish(message)) is not None)
        if advised or carried.get("fromAdvice") is True:
            return {"fromAdvice": True}
        if carried.get("amount") is not None:
            return {"amount": carried["amount"], "currencyCode": LIRA}
        return None
    # CHAT-09 K6-B: only lira can run, so an amount with no currency beside it is lira (the answer's "Aylık tutar" line shows it);
    # another currency beside it is refused, never converted (INC-03 K8-A).
    if amount_currency_in(message) not in (None, LIRA):
        return "lira_only"
    return {"amount": said, "currencyCode": LIRA}


def monthly_follow_up(message: str, previous: LastCall | None) -> dict | None:
    """The last monthly saving's arguments when ``message`` is a "peki/ya" follow-up of it that changes only its figures ("Peki 6
    ay?", "Ya 2.000 TL olsaydı?"); None otherwise (INV-03 D1-B, K7-A).

    Code chooses this call, as it does the advised saving's: "6 ay" is also the aylık seri's count, and the small model would be
    free to send it there. A period or a category named in the follow-up is another question, left to the usual path.
    """
    if previous is None or previous.tool != MONTHLY_TOOL or not opens_follow_up(message):
        return None
    if not (months_in(message) or any(a > 0 for a in amounts_in(message))):
        return None
    if period_in(message) is not None or category_in(message) is not None:
        return None
    return previous.arguments


def carried_monthly(message: str, previous: LastCall | None) -> dict | None:
    """The last monthly saving's arguments a message may keep: only after "peki/ya" (CHAT-10 rule for the user's figures)."""
    if previous is None or previous.tool != MONTHLY_TOOL or not opens_follow_up(message):
        return None
    return previous.arguments


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


# The counts inside the tools' period names: the 3 of "son 3 ayda" is not an amount (K18-B).
_PERIOD_COUNTS = {"Last3Months": 3, "Last6Months": 6, "Last7Days": 7, "Last30Days": 30}


def scenario_by_figures(message: str) -> str | None:
    """The "what if" the user's figures point to when no scenario word did (CHAT-09 K18-B): a period, date or category belongs
    to the one-off (the monthly saving takes none), an amount to the monthly saving (the one-off takes none). None for both or
    neither: then the user is asked. ``message`` has its dates already replaced by a period name (CHAT-08)."""
    # INV-03 PR2: "6 ay" is the monthly saving's duration, not a one-off's period.
    one_off = mentions_period(without_durations(message)) or category_in(message) is not None
    counts = {_PERIOD_COUNTS.get(period) for period in periods_in(message)}
    monthly = any(amount > 0 and amount not in counts for amount in amounts_in(message))
    return ONE_OFF_TOOL if one_off and not monthly else MONTHLY_TOOL if monthly and not one_off else None


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
