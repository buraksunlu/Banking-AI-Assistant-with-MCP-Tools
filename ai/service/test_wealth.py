from datetime import date

import pytest

from conversation import LastCall
from grounding import amount_currency_in, check_arguments
from wealth import MONTH_TOOLS, MonthlyPlan, advised_saving_call, carried_monthly, monthly_follow_up, is_complete_month, needs_month_question, plan_monthly

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


def plan(arguments, message, after_advice=False, carried=None):
    return plan_monthly(arguments, message, after_advice, carried)


def test_an_amount_its_lira_currency_and_a_duration_run_as_they_are():
    result = plan({"amount": 1000, "currencyCode": "TRY"}, "Ayda 1.000 TL 3 ay altın olarak biriktirseydim ne olurdu?")
    assert result == MonthlyPlan(run={"amount": 1000, "currencyCode": "TRY", "months": 3})


def test_the_model_cannot_choose_the_amount():
    result = plan({"amount": 1000, "currencyCode": "TRY"}, "Ayda biriktirseydim ne olurdu?")
    # CHAT-09 K6-B: the currency is not asked, since only lira can run; INV-03 K2-A: the duration is asked after the amount.
    assert result == MonthlyPlan(ask="amount", known={}, later=("months",))


def test_a_currency_the_model_wrote_is_not_the_users():
    # The server's enum has only TRY, so the model writes it for "100 dolar" too: the message decides.
    result = plan({"amount": 100, "currencyCode": "TRY"}, "Ayda 100 dolar biriktirseydim")
    assert result == MonthlyPlan(lira_only=True)


@pytest.mark.parametrize("message", ["Ayda 1.000 biriktirseydim dolar olarak ne olurdu?", "Ayda 5 bin biriktirseydim ne olurdu?"])
def test_an_amount_without_a_currency_is_lira(message):
    # CHAT-09 K6-B (INC-03 K17-B for the monthly saving): the scenario takes lira only, so a bare amount is lira and is not asked;
    # the answer's "Aylık tutar" line shows it as lira. "dolar olarak" names the asset, not the amount's currency.
    amount = 5000 if "bin" in message else 1000
    assert plan({"amount": amount}, message + " 12 ay") == MonthlyPlan(run={"amount": amount, "currencyCode": "TRY", "months": 12})


def test_from_advice_runs_when_the_user_asks_for_the_advised_amount():
    result = plan({"fromAdvice": True}, "Önerdiğin tutarı her ay 6 ay boyunca biriktirseydim ne olurdu?")
    assert result == MonthlyPlan(run={"fromAdvice": True, "months": 6})


def test_from_advice_runs_in_a_follow_up_of_an_advice_answer():
    # INV-03 K9-A: the advised amount needs its duration too.
    assert plan({"fromAdvice": True}, "Peki bu tutarı biriktirseydim?", after_advice=True) == MonthlyPlan(
        ask="months", known={"fromAdvice": True})


def test_from_advice_the_user_did_not_ask_for_is_a_guess():
    result = plan({"fromAdvice": True}, "Ayda biriktirseydim ne olurdu?")
    assert result == MonthlyPlan(ask="amount", known={}, later=("months",))


def test_an_amount_the_user_wrote_wins_over_the_advice_flag():
    result = plan({"amount": 236, "fromAdvice": True}, "Önerdiğin 236 TL'yi 12 ay biriktirseydim ne olurdu?")
    assert result == MonthlyPlan(run={"amount": 236, "currencyCode": "TRY", "months": 12})


# --- INV-03 PR2: the duration (K2-A asked when unsaid, K5-A read from the words, K6-A limits, K7-A "peki", D5-A two durations) ---

LIRA_1000 = {"amount": 1000, "currencyCode": "TRY"}
LIMITS = "En az 2, en fazla 22 ay için hesaplayabilirim."


@pytest.mark.parametrize(("message", "expected"), [
    ("Ayda 1.000 TL biriktirseydim ne olurdu?", MonthlyPlan(ask="months", known=LIRA_1000)),
    ("Ayda 1.000 TL 2 yıl biriktirseydim ne olurdu?", MonthlyPlan(ask="months", known=LIRA_1000, note=LIMITS)),
    ("Ayda 1.000 TL 1 ay biriktirseydim ne olurdu?", MonthlyPlan(ask="months", known=LIRA_1000, note=LIMITS)),
    ("Ayda 1.000 TL 6 ay ya da 1 yıl biriktirseydim?", MonthlyPlan(ask="months", known=LIRA_1000)),
    ("Ayda 1.000 TL 22 ay biriktirseydim ne olurdu?", MonthlyPlan(run={**LIRA_1000, "months": 22})),
    ("Ayda 1.000 TL bir buçuk yıl biriktirseydim?", MonthlyPlan(run={**LIRA_1000, "months": 18})),
])
def test_the_duration_is_the_users_own_and_within_the_limits(message, expected):
    assert plan(LIRA_1000, message) == expected


def test_a_duration_the_model_wrote_is_not_the_users():
    # D2-A: the model is not shown the duration; one it writes anyway is ignored like an invented amount.
    assert plan({**LIRA_1000, "months": 12}, "Ayda 1.000 TL biriktirseydim") == MonthlyPlan(ask="months", known=LIRA_1000)


def test_a_said_duration_is_kept_while_the_amount_is_asked():
    assert plan({}, "6 ay biriktirseydim ne olurdu?") == MonthlyPlan(ask="amount", known={"months": 6})


def test_a_duration_outside_the_limits_is_asked_first_with_its_note():
    assert plan({}, "30 ay biriktirseydim ne olurdu?") == MonthlyPlan(ask="months", known={}, later=("amount",), note=LIMITS)


LAST = {**LIRA_1000, "months": 12}


@pytest.mark.parametrize(("message", "carried", "expected"), [
    ("Peki 6 ay?", LAST, {**LIRA_1000, "months": 6}),
    ("Peki 2.000 TL?", LAST, {"amount": 2000, "currencyCode": "TRY", "months": 12}),
    ("Ya 1 yıl 5 bin olsaydı?", LAST, {"amount": 5000, "currencyCode": "TRY", "months": 12}),
    ("Peki 3 ay?", {"fromAdvice": True, "months": 12}, {"fromAdvice": True, "months": 3}),
])
def test_a_follow_up_changes_what_it_says_and_keeps_the_rest(message, carried, expected):
    # K7-A: "peki 6 ay?" changes the duration, "peki 2.000 TL?" the amount.
    assert plan({}, message, carried=carried) == MonthlyPlan(run=expected)


def test_a_follow_up_that_changes_nothing_runs_the_carried_call():
    assert plan({}, "Peki?", carried=LAST) == MonthlyPlan(run=LAST)


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


SAVED = LastCall("what_if_monthly", {"amount": 1000, "currencyCode": "TRY", "months": 12}, "Ayda 1.000 TL 12 ay biriktirseydim?")


@pytest.mark.parametrize(("message", "previous", "taken"), [
    ("Peki 6 ay?", SAVED, True),
    ("Ya 2.000 TL olsaydı?", SAVED, True),
    ("Peki 1 yıl 5 bin?", SAVED, True),
    ("6 ay?", SAVED, False),                       # no "peki/ya": the user's figures are not carried (CHAT-10)
    ("Peki son 3 ay?", SAVED, False),              # a period: another question
    ("Peki market için 6 ay?", SAVED, False),      # a category: another question
    ("Peki kategorilere göre?", SAVED, False),     # no figure
    ("Peki 6 ay?", ADVICE_ANSWER, False),          # not after a monthly saving
    ("Peki 6 ay?", None, False),
])
def test_a_follow_up_of_a_monthly_saving_that_changes_its_figures_is_codes(message, previous, taken):
    assert (monthly_follow_up(message, previous) is not None) is taken


def test_only_a_peki_message_carries_the_monthly_savings_figures():
    assert carried_monthly("Peki 6 ay?", SAVED) == SAVED.arguments
    assert carried_monthly("Ayda 2.000 TL biriktirseydim?", SAVED) is None
    assert carried_monthly("Peki 6 ay?", ADVICE_ANSWER) is None


def test_a_duration_points_to_no_one_off():
    from wealth import scenario_by_figures
    assert scenario_by_figures("5.000 TL 6 ay biriktirseydim ne olurdu?") == "what_if_monthly"
    assert scenario_by_figures("6 ay biriktirseydim ne olurdu?") is None
    assert scenario_by_figures("Geçen ay 5.000 TL biriktirseydim ne olurdu?") is None
