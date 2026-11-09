from datetime import date

import pytest

from conversation import LastCall
from grounding import amount_currency_in, check_arguments
from wealth import MONTH_TOOLS, MonthlyPlan, advised_saving_call, is_complete_month, needs_month_question, plan_monthly

# INC-03 PR2: what the chat checks in code before the four income, advice and what-if tools run. Decisions K8-A (the amount is the
# user's own and in lira), K9-A (fromAdvice only when asked for or after an advice answer), K15-A (a complete month, asked
# otherwise), K17-B (the currency is the one written next to the amount). Expected values by hand.


@pytest.mark.parametrize(("message", "currency"), [
    ("Ayda 1.000 TL biriktirseydim", "TRY"),
    ("1.000 TL'yi dolar olarak biriktirseydim", "TRY"),        # "dolar" is the asset, TL is the amount's currency
    ("Ayda 100 dolar biriktirseydim", "USD"),
    ("Ayda $100 biriktirseydim", "USD"),
    ("Ayda 2.500,50 lira biriktirseydim", "TRY"),
    ("Ayda 5 bin TL altın olarak biriktirseydim", "TRY"),
    ("Ayda 500 euro biriktirseydim", "EUR"),
    ("Ayda 1000 biriktirseydim dolar olarak", None),            # no currency next to the amount: asked, never guessed
    ("Ayda biriktirseydim", None),
])
def test_the_currency_is_the_one_next_to_the_amount(message, currency):
    assert amount_currency_in(message) == currency


def test_an_optional_amount_the_user_did_not_write_is_dropped():
    # The schema says ["number", "null"]: the model's own 1000 was accepted as if the user had said it.
    schema = {"properties": {"amount": {"type": ["number", "null"]}, "currencyCode": {"type": ["string", "null"]}}, "required": []}

    invented = check_arguments({"amount": 1000, "currencyCode": "TRY"}, schema, "Ayda biriktirseydim ne olurdu?")
    said = check_arguments({"amount": 1000, "currencyCode": "TRY"}, schema, "Ayda 1.000 TL biriktirseydim ne olurdu?")

    assert "amount" not in invented.arguments and said.arguments == {"amount": 1000, "currencyCode": "TRY"}


def plan(arguments, message, after_advice=False):
    return plan_monthly(arguments, message, after_advice)


def test_an_amount_and_its_lira_currency_run_as_they_are():
    result = plan({"amount": 1000, "currencyCode": "TRY"}, "Ayda 1.000 TL altın olarak biriktirseydim ne olurdu?")
    assert result == MonthlyPlan(run={"amount": 1000, "currencyCode": "TRY"})


def test_the_model_cannot_choose_the_amount():
    result = plan({"amount": 1000, "currencyCode": "TRY"}, "Ayda biriktirseydim ne olurdu?")
    # CHAT-09 K6-B: only the amount is asked; the currency is not, since only lira can run.
    assert result == MonthlyPlan(ask="amount", known={})


def test_a_currency_the_model_wrote_is_not_the_users():
    # The server's enum has only TRY, so the model writes it for "100 dolar" too: the message decides.
    result = plan({"amount": 100, "currencyCode": "TRY"}, "Ayda 100 dolar biriktirseydim")
    assert result == MonthlyPlan(lira_only=True)


@pytest.mark.parametrize("message", ["Ayda 1.000 biriktirseydim dolar olarak ne olurdu?", "Ayda 5 bin biriktirseydim ne olurdu?"])
def test_an_amount_without_a_currency_is_lira(message):
    # CHAT-09 K6-B (INC-03 K17-B for the monthly saving): the scenario takes lira only, so a bare amount is lira and is not asked;
    # the answer's "Aylık tutar" line shows it as lira. "dolar olarak" names the asset, not the amount's currency.
    amount = 5000 if "bin" in message else 1000
    assert plan({"amount": amount}, message) == MonthlyPlan(run={"amount": amount, "currencyCode": "TRY"})


def test_from_advice_runs_when_the_user_asks_for_the_advised_amount():
    result = plan({"fromAdvice": True}, "Önerdiğin tutarı her ay biriktirseydim ne olurdu?")
    assert result == MonthlyPlan(run={"fromAdvice": True})


def test_from_advice_runs_in_a_follow_up_of_an_advice_answer():
    assert plan({"fromAdvice": True}, "Peki bu tutarı biriktirseydim?", after_advice=True) == MonthlyPlan(run={"fromAdvice": True})


def test_from_advice_the_user_did_not_ask_for_is_a_guess():
    result = plan({"fromAdvice": True}, "Ayda biriktirseydim ne olurdu?")
    assert result == MonthlyPlan(ask="amount", known={})


def test_an_amount_the_user_wrote_wins_over_the_advice_flag():
    result = plan({"amount": 236, "fromAdvice": True}, "Önerdiğin 236 TL'yi biriktirseydim ne olurdu?")
    assert result == MonthlyPlan(run={"amount": 236, "currencyCode": "TRY"})


# --- K15-A: a complete month ------------------------------------------------------------------------------------------------

TODAY = date(2026, 10, 10)


@pytest.mark.parametrize(("start", "end", "ok"), [
    (date(2026, 9, 1), date(2026, 9, 30), True),
    (date(2026, 2, 1), date(2026, 2, 28), True),
    (date(2026, 10, 1), date(2026, 10, 31), False),    # the running month
    (date(2026, 9, 2), date(2026, 9, 30), False),
    (date(2026, 9, 1), date(2026, 9, 29), False),
    (date(2026, 8, 1), date(2026, 9, 30), False),
])
def test_only_a_whole_past_calendar_month_is_complete(start, end, ok):
    assert is_complete_month(start, end, TODAY) is ok


@pytest.mark.parametrize(("message", "asked"), [
    ("Geçen ay maaşımın ne kadarını harcadım?", False),
    ("Bu ay maaşımın ne kadarını harcadım?", True),     # the model can only write LastMonth: without this it answered another month
    ("Son 3 ayda gelirimin ne kadarını harcadım?", True),
    ("Bu hafta maaşımın ne kadarını harcadım?", True),
    ("Haftada maaşımın ne kadarını harcıyorum?", True),
    ("Maaşımın ne kadarını harcadım?", True),             # no period at all
])
def test_a_month_tool_asks_unless_the_user_said_last_month(message, asked):
    assert needs_month_question(message, {"period": "LastMonth"}, None) is asked


def test_a_follow_up_that_says_nothing_keeps_the_last_calls_month():
    last = LastCall("get_income_share", {"period": "LastMonth"}, "Geçen ay maaşımın ne kadarını harcadım?")
    assert needs_month_question("Peki nerede tasarruf edebilirim?", {"period": "LastMonth"}, last) is False


def test_the_month_tools_are_the_two_complete_month_analyses():
    assert MONTH_TOOLS == ("get_income_share", "get_spending_advice")


@pytest.mark.parametrize(("today", "ok"), [(date(2026, 9, 30), False), (date(2026, 10, 1), True)])
def test_a_month_is_complete_only_after_its_last_day(today, ok):
    # On the last day itself salary and spending may still change (INC-01 S1-A): the day after, it is over.
    assert is_complete_month(date(2026, 9, 1), date(2026, 9, 30), today) is ok


# --- A call the code chooses from the words (measurement W12/W13: the small model never picked it) ----------------------------

ADVICE_ANSWER = LastCall("get_spending_advice", {"period": "LastMonth"}, "Geçen ay nerede tasarruf edebilirim?")


@pytest.mark.parametrize(("message", "previous", "expected"), [
    ("Önerdiğin tutarı her ay biriktirseydim ne olurdu?", None, {"fromAdvice": True}),
    ("Peki önerdiğin tutarı biriktirseydim?", ADVICE_ANSWER, {"fromAdvice": True}),
    ("Peki bu tutarı biriktirseydim?", ADVICE_ANSWER, {"fromAdvice": True}),
    ("Peki bu tutarı biriktirseydim?", None, None),                  # "this amount" of what? only an advice answer gives it meaning
    ("Ayda 1.000 TL biriktirseydim ne olurdu?", ADVICE_ANSWER, None),   # a figure of the user's own is never replaced
    ("Önerdiğin tutarı biriktirirsem ne olur?", None, None),          # not a what-if: planning, no past prices
    ("Önerdiğin tutarı harcasaydım ne olurdu?", None, None),
])
def test_the_advised_saving_is_chosen_by_code_from_the_words(message, previous, expected):
    call = advised_saving_call(message, previous)
    assert (None if call is None else call[1]) == expected and (call is None or call[0] == "what_if_monthly")
