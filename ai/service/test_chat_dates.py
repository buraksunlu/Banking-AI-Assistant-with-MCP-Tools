"""CHAT-08 PR2: a question with a date or a range of dates, from the user's words to the MCP call.

The date is read by code, the model is shown "bu ay" in its place (K10-A) and never a date, and the call is run with the range
code read (K2-B). Expected dates are worked out by hand from the demo moment, Saturday 10 October 2026.
"""

from datetime import date
from decimal import Decimal

from chat import ChatReply, ToolCall
from clarification import PERIOD_OPTIONS
from dates import CustomerDay, DateReading, check_range
from ollama_client import OllamaUnavailable
from prompts import OUT_OF_SCOPE_TEXT
from suggestions import OUT_OF_SCOPE_SUGGESTIONS
from test_chat import ANSWER, THIS_MONTH_SUMMARY, TOKEN, YESTERDAY, YESTERDAY_CATEGORIES, FakeGateway, service

DAY = CustomerDay(date(2026, 10, 10), "Europe/Istanbul")
THIS_MONTH = ToolCall("get_spending_summary", {"period": "ThisMonth"})
FIRST_FIVE_DAYS = {"period": "Custom", "from": "2026-09-01", "to": "2026-09-05"}
LAST_MONTH = ToolCall("get_spending_summary", {"period": "LastMonth"})

# Murat's 1-5 September 2026, by hand from docs/synthetic-seed.md: two purchases on the 3rd, 1.640,50 + 3.476,00 TL.
SEPTEMBER_FIRST_FIVE = {"period": {"kind": "Custom", "startDate": "2026-09-01", "endDate": "2026-09-05"},
                        "currencies": [{"currencyCode": "TRY", "totalAmount": Decimal("5116.50"), "transactionCount": 2,
                                        "uncategorizedAmount": Decimal("0.00"), "uncategorizedCount": 0}]}
LAST_MONTH_SUMMARY = {"period": {"kind": "LastMonth", "startDate": "2026-09-01", "endDate": "2026-09-30"},
                      "currencies": [{"currencyCode": "TRY", "totalAmount": Decimal("53388.39"), "transactionCount": 20,
                                      "uncategorizedAmount": Decimal("0.00"), "uncategorizedCount": 0}]}


def dated(calls=None, results=None, **options):
    return service([], gateway=FakeGateway(results or SEPTEMBER_FIRST_FIVE), turns=calls or [[THIS_MONTH]], **options)


# --- The call ----------------------------------------------------------------------------------------------------------

def test_a_range_in_the_message_becomes_a_custom_call_with_the_dates_code_read():
    chat, selector, gateway, explainer = dated()

    reply = chat.answer("1-5 Eylül arası ne kadar harcadım?", TOKEN, DAY)

    assert gateway.calls[0][:2] == ("get_spending_summary", FIRST_FIVE_DAYS)
    assert reply.tool_calls == [ToolCall("get_spending_summary", FIRST_FIVE_DAYS)]
    assert reply.type == "answer"


def test_the_model_is_shown_a_period_name_in_the_place_of_the_dates():
    chat, selector, gateway, explainer = dated()

    chat.answer("Migros'a 1-5 Eylül arası ne kadar harcadım?", TOKEN, DAY)

    assert selector.calls[0][0] == "Migros'a bu ay ne kadar harcadım?"
    assert "Eylül" not in repr(selector.calls) and "2026-09" not in repr(selector.calls)
    # The explanation, in turn, is asked the user's own question.
    assert explainer.calls[0][0] == "Migros'a 1-5 Eylül arası ne kadar harcadım?"


def test_whatever_period_the_model_wrote_is_replaced_by_the_range():
    chat, _, gateway, _ = dated(calls=[[ToolCall("get_spending_summary", {"period": "Last30Days"})]])

    chat.answer("1 Eylül'den 5 Eylül'e kadar ne harcadım?", TOKEN, DAY)

    assert gateway.calls[0][1] == FIRST_FIVE_DAYS


def test_a_single_day_is_a_range_of_one_day():
    chat, _, gateway, _ = dated(results=YESTERDAY)

    chat.answer("1 Eylül'de ne kadar harcadım?", TOKEN, DAY)

    assert gateway.calls[0][1] == {"period": "Custom", "from": "2026-09-01", "to": "2026-09-01"}


def test_a_whole_month_is_read_as_its_days_and_the_running_month_ends_today():
    chat, _, gateway, _ = dated(calls=[[THIS_MONTH], [THIS_MONTH]], results=[SEPTEMBER_FIRST_FIVE, THIS_MONTH_SUMMARY])

    chat.answer("Eylül'de ne harcadım?", TOKEN, DAY)
    chat.answer("Ekim'de ne harcadım?", TOKEN, DAY)

    assert gateway.calls[0][1] == {"period": "Custom", "from": "2026-09-01", "to": "2026-09-30"}
    assert gateway.calls[1][1] == {"period": "Custom", "from": "2026-10-01", "to": "2026-10-10"}


def test_the_answer_starts_with_the_period_that_was_run():
    chat, _, _, _ = dated()

    reply = chat.answer("1-5 Eylül arası ne kadar harcadım?", TOKEN, DAY)

    # Fixed by code, after the answer check: the user sees which days were meant (K16-A).
    assert reply.answer == f"Dönem: 1-5 Eylül 2026.\n\n{ANSWER}"


def test_a_year_less_date_is_the_most_recent_past_one():
    chat, _, gateway, _ = dated(results=YESTERDAY)

    reply = chat.answer("15 Kasım'da ne harcadım?", TOKEN, DAY)

    assert gateway.calls[0][1] == {"period": "Custom", "from": "2025-11-15", "to": "2025-11-15"}
    assert reply.answer.startswith("Dönem: 15 Kasım 2025.\n\n")


def test_without_the_customers_day_dates_are_not_read():
    chat, selector, gateway, _ = dated(calls=[[THIS_MONTH]], results=THIS_MONTH_SUMMARY)

    chat.answer("1-5 Eylül arası ne kadar harcadım?", TOKEN)

    assert selector.calls[0][0] == "1-5 Eylül arası ne kadar harcadım?"
    assert gateway.calls[0][1] == {"period": "ThisMonth"}


# --- Tools that take no dates (K11-B) -----------------------------------------------------------------------------------

def test_a_tool_without_dates_is_never_run_with_a_guessed_period():
    chat, _, gateway, explainer = dated(calls=[[ToolCall("get_monthly_trend", {"months": 6})]])

    reply = chat.answer("Eylül'de aylara göre nasıl değişti?", TOKEN, DAY)

    assert gateway.calls == [] and explainer.calls == []
    assert (reply.type, reply.tool_calls, reply.facts) == ("outOfScope", [], [])
    assert "belirli bir tarih" in reply.answer
    assert reply.suggestions == list(OUT_OF_SCOPE_SUGGESTIONS)


# --- A date that is impossible or cannot be read (K1-C, K5-B, K12-C) --------------------------------------------------------

def test_an_impossible_date_is_asked_back_with_the_reason_and_runs_nothing():
    chat, selector, gateway, _ = dated()

    reply = chat.answer("31 Şubat'ta ne harcadım?", TOKEN, DAY)

    assert (reply.type, selector.calls, gateway.calls) == ("clarification", [], [])
    assert reply.answer.startswith("31 Şubat diye bir gün yok.")
    assert all(label in reply.answer for label, _ in PERIOD_OPTIONS)


def test_a_date_in_the_future_is_asked_back():
    chat, selector, _, _ = dated()

    reply = chat.answer("1 Kasım 2026'da ne harcadım?", TOKEN, DAY)

    assert reply.type == "clarification" and "henüz gelmedi" in reply.answer and selector.calls == []


def test_a_date_code_cannot_read_is_asked_back_when_there_is_no_second_step():
    chat, selector, _, _ = dated()

    reply = chat.answer("Geçen salı ne harcadım?", TOKEN, DAY)

    assert reply.type == "clarification" and selector.calls == []
    assert reply.answer.startswith("Tarihi anlayamadım.")


def test_the_reply_to_a_date_question_completes_the_original_question():
    chat, selector, gateway, _ = dated(calls=[[LAST_MONTH]], results=LAST_MONTH_SUMMARY)
    chat.answer("31 Şubat'ta ne harcadım?", TOKEN, DAY)

    reply = chat.answer("geçen ay", TOKEN, DAY)

    # The words of the impossible date are gone from what the model reads; the reply is added to the rest.
    assert selector.calls[0][0] == "ne harcadım? (geçen ay)"
    assert gateway.calls[0][1] == {"period": "LastMonth"} and reply.type == "answer"


def test_the_reply_may_be_a_number_of_the_offered_periods():
    chat, selector, gateway, _ = dated(calls=[[LAST_MONTH]], results=LAST_MONTH_SUMMARY)
    chat.answer("31 Şubat'ta ne harcadım?", TOKEN, DAY)

    chat.answer("2", TOKEN, DAY)

    assert selector.calls[0][0] == "ne harcadım? (geçen ay)"


def test_the_reply_may_be_another_date():
    chat, selector, gateway, _ = dated()
    chat.answer("31 Şubat'ta ne harcadım?", TOKEN, DAY)

    chat.answer("1-5 Eylül", TOKEN, DAY)

    assert selector.calls[0][0] == "ne harcadım? (bu ay)"
    assert gateway.calls[0][1] == FIRST_FIVE_DAYS


def test_a_reply_that_names_no_period_is_a_new_question():
    chat, selector, _, _ = dated(calls=[[]])
    chat.answer("31 Şubat'ta ne harcadım?", TOKEN, DAY)

    reply = chat.answer("Bana bir şey söyle", TOKEN, DAY)

    assert reply.type == "outOfScope" and selector.calls[0][0] == "Bana bir şey söyle"


def test_a_spending_question_without_a_period_is_asked_back_instead_of_called_out_of_scope():
    chat, selector, gateway, _ = dated(calls=[[]], results=LAST_MONTH_SUMMARY)

    reply = chat.answer("Ne harcadım?", TOKEN, DAY)

    # K6-C: the model chose no tool, but this is a spending question whose period is missing.
    assert reply.type == "clarification" and gateway.calls == []
    assert "Hangi dönem için?" in reply.answer


def test_a_message_that_is_not_about_spending_is_still_out_of_scope():
    chat, _, _, _ = dated(calls=[[]])

    assert chat.answer("Merhaba", TOKEN, DAY) == ChatReply("outOfScope", OUT_OF_SCOPE_TEXT, [], [], list(OUT_OF_SCOPE_SUGGESTIONS))


def test_a_question_that_names_its_period_is_out_of_scope_when_no_tool_fits():
    chat, _, _, _ = dated(calls=[[]])

    assert chat.answer("Bu ay ne harcadım?", TOKEN, DAY).type == "outOfScope"


# --- A date as the answer to a period question ----------------------------------------------------------------------------

def test_a_date_answers_the_question_about_a_missing_period():
    chat, selector, gateway, _ = dated(calls=[[THIS_MONTH]])
    asked = chat.answer("Ne kadar harcadım?", TOKEN, DAY)
    assert asked.type == "clarification"

    reply = chat.answer("1 Eylül ile 5 Eylül arası", TOKEN, DAY)

    # The tool was already chosen; only its period is completed, and the model is not asked again.
    assert len(selector.calls) == 1
    assert gateway.calls[0][:2] == ("get_spending_summary", FIRST_FIVE_DAYS)
    assert reply.type == "answer" and reply.answer.startswith("Dönem: 1-5 Eylül 2026.")


# --- Follow-ups (K7-A) ------------------------------------------------------------------------------------------------------

def test_the_model_reads_the_last_range_as_a_period_name_in_a_follow_up():
    results = [SEPTEMBER_FIRST_FIVE, YESTERDAY_CATEGORIES]
    chat, selector, gateway, _ = dated(calls=[[THIS_MONTH], [ToolCall("get_category_breakdown", {"period": "ThisMonth"})]],
                                       results=results)
    chat.answer("1-5 Eylül arası ne kadar harcadım?", TOKEN, DAY)

    chat.answer("Peki kategorilere göre?", TOKEN, DAY)

    context = selector.contexts[1]
    assert (context.tool, context.arguments) == ("get_spending_summary", {"period": "ThisMonth"})
    assert "Custom" not in repr(selector.contexts[1]) and "2026-09" not in repr(selector.calls)


def test_a_follow_up_that_keeps_the_period_gets_the_last_range_back():
    results = [SEPTEMBER_FIRST_FIVE, YESTERDAY_CATEGORIES]
    chat, selector, gateway, _ = dated(calls=[[THIS_MONTH], [ToolCall("get_category_breakdown", {"period": "ThisMonth"})]],
                                       results=results)
    chat.answer("1-5 Eylül arası ne kadar harcadım?", TOKEN, DAY)

    reply = chat.answer("Peki kategorilere göre?", TOKEN, DAY)

    assert gateway.calls[1][:2] == ("get_category_breakdown", FIRST_FIVE_DAYS)
    assert reply.tool_calls == [ToolCall("get_category_breakdown", FIRST_FIVE_DAYS)]
    assert reply.answer.startswith("Dönem: 1-5 Eylül 2026.")


def test_a_follow_up_with_a_period_of_its_own_does_not_get_the_range():
    chat, _, gateway, _ = dated(calls=[[THIS_MONTH], [LAST_MONTH]], results=[SEPTEMBER_FIRST_FIVE, LAST_MONTH_SUMMARY])
    chat.answer("1-5 Eylül arası ne kadar harcadım?", TOKEN, DAY)

    chat.answer("Peki geçen ay?", TOKEN, DAY)

    assert gateway.calls[1][1] == {"period": "LastMonth"}


def test_a_follow_up_that_says_this_month_means_this_month():
    chat, _, gateway, _ = dated(calls=[[THIS_MONTH], [THIS_MONTH]], results=[SEPTEMBER_FIRST_FIVE, THIS_MONTH_SUMMARY])
    chat.answer("1-5 Eylül arası ne kadar harcadım?", TOKEN, DAY)

    chat.answer("Peki bu ay?", TOKEN, DAY)

    assert gateway.calls[1][1] == {"period": "ThisMonth"}


def test_a_follow_up_with_a_new_range_replaces_the_old_one():
    second = {**SEPTEMBER_FIRST_FIVE, "period": {"kind": "Custom", "startDate": "2026-09-06", "endDate": "2026-09-10"}}
    chat, selector, gateway, _ = dated(calls=[[THIS_MONTH], [THIS_MONTH]], results=[SEPTEMBER_FIRST_FIVE, second])
    chat.answer("1-5 Eylül arası ne kadar harcadım?", TOKEN, DAY)

    reply = chat.answer("Peki 6-10 Eylül?", TOKEN, DAY)

    assert selector.calls[1][0] == "Peki bu ay?"
    assert gateway.calls[1][1] == {"period": "Custom", "from": "2026-09-06", "to": "2026-09-10"}
    assert reply.answer.startswith("Dönem: 6-10 Eylül 2026.")


def test_an_offered_follow_up_keeps_the_range():
    chat, selector, gateway, _ = dated(results=[SEPTEMBER_FIRST_FIVE, YESTERDAY_CATEGORIES])
    first = chat.answer("1-5 Eylül arası ne kadar harcadım?", TOKEN, DAY)
    assert "Kategorilere göre?" in first.suggestions

    reply = chat.answer("Kategorilere göre?", TOKEN, DAY)

    assert len(selector.calls) == 1
    assert gateway.calls[1][:2] == ("get_category_breakdown", FIRST_FIVE_DAYS)
    assert reply.answer.startswith("Dönem: 1-5 Eylül 2026.")


# --- The second step (PR3, K1-C, K3-D1) -----------------------------------------------------------------------------------------

class FakeInterpreter:
    def __init__(self, result=None, error=None):
        self.result, self.error, self.calls = result, error, []

    def interpret(self, expression, day, deadline):
        self.calls.append((expression, day))
        if self.error:
            raise self.error
        return self.result


LAST_TUESDAY = check_range("2026-10-06", "2026-10-06", DAY.today)


def with_interpreter(interpreter, **options):
    chat, selector, gateway, explainer = dated(**options)
    chat.interpreter = interpreter
    return chat, selector, gateway, explainer, interpreter


def test_a_date_the_model_reads_is_run_like_one_code_read():
    chat, selector, gateway, _, interpreter = with_interpreter(FakeInterpreter(LAST_TUESDAY), results=YESTERDAY)

    reply = chat.answer("Geçen salı ne harcadım?", TOKEN, DAY)

    assert interpreter.calls == [("Geçen salı", DAY)]
    # Only the words of the date go to the second step, and the first model sees a period name, as for any date.
    assert selector.calls[0][0] == "bu ay ne harcadım?"
    assert gateway.calls[0][1] == {"period": "Custom", "from": "2026-10-06", "to": "2026-10-06"}
    # The user sees which day it was taken to be (K16-A).
    assert reply.answer.startswith("Dönem: 6 Ekim 2026.\n\n")


def test_an_expression_the_model_does_not_understand_is_asked_back():
    chat, selector, gateway, _, interpreter = with_interpreter(FakeInterpreter(None))

    reply = chat.answer("Bayramda ne harcadım?", TOKEN, DAY)

    assert (reply.type, selector.calls, gateway.calls) == ("clarification", [], [])
    assert reply.answer.startswith("Tarihi anlayamadım.")


def test_a_range_the_checks_refuse_is_asked_back_with_the_reason():
    future = check_range("2026-10-11", "2026-10-11", DAY.today)
    chat, selector, gateway, _, _ = with_interpreter(FakeInterpreter(future))

    reply = chat.answer("Geçen salı ne harcadım?", TOKEN, DAY)

    assert reply.type == "clarification" and "henüz gelmedi" in reply.answer and gateway.calls == []


def test_a_model_that_is_down_means_the_period_is_asked():
    chat, _, gateway, _, _ = with_interpreter(FakeInterpreter(error=OllamaUnavailable("down")))

    reply = chat.answer("Geçen salı ne harcadım?", TOKEN, DAY)

    assert reply.type == "clarification" and gateway.calls == []


def test_the_second_step_is_not_asked_for_what_code_can_read_or_for_no_date():
    chat, _, _, _, interpreter = with_interpreter(FakeInterpreter(LAST_TUESDAY), calls=[[THIS_MONTH], [THIS_MONTH]],
                                                  results=[SEPTEMBER_FIRST_FIVE, THIS_MONTH_SUMMARY])

    chat.answer("1-5 Eylül arası ne kadar harcadım?", TOKEN, DAY)
    chat.answer("Bu ay ne kadar harcadım?", TOKEN, DAY)

    assert interpreter.calls == []


def test_a_relative_date_may_answer_the_question_about_a_missing_period():
    chat, selector, gateway, _, interpreter = with_interpreter(FakeInterpreter(LAST_TUESDAY), results=YESTERDAY)
    chat.answer("Ne kadar harcadım?", TOKEN, DAY)

    reply = chat.answer("geçen salı", TOKEN, DAY)

    assert len(selector.calls) == 1
    assert gateway.calls[0][1] == {"period": "Custom", "from": "2026-10-06", "to": "2026-10-06"}
    assert reply.answer.startswith("Dönem: 6 Ekim 2026.")


def test_the_real_service_has_the_second_step():
    from app import create_app
    from chat import LangChainDateInterpreter
    from test_app import ENV

    assert isinstance(create_app(ENV).state.chat_service.interpreter, LangChainDateInterpreter)
