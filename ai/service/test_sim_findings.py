"""The simulator trial of 17 Nov 2026 (after CHAT-13): five findings, each reproduced with the real model before the fix.
Decisions K1-A (the balance card says no sentence), K2-B (the last three complete months and a row to write another), K3-C (the
advice card's rows: short label, amount, its share under it), K4-C (a category the model guessed is no question about months),
K5-C (a reply that misses the question it answers is never read with an older call)."""

from datetime import date

import pytest

from cards import cards_for
from chat import ToolCall
from conversation import LastCall
from dates import CustomerDay
from routing import GENERAL, Route, Router
from spelling import correct
import test_chat
from test_chat import TOKEN, FakeGateway, rows, service
from test_chat_brands import CATEGORY_DETAIL
from test_chat_accounts import AccountGateway
from test_chat_wealth import ADVICE, INCOME_SHARE, wealth
from test_facts_accounts import MURAT_ACCOUNTS
from test_facts_wealth import advice, advice_row, income, income_row

DAY = CustomerDay(date(2026, 10, 10), "Europe/Istanbul")
MARCH = {"period": "Custom", "from": "2026-03-01", "to": "2026-03-31"}
SUMMARY = {"period": {"kind": "Custom", "startDate": "2026-03-01", "endDate": "2026-03-31"}, "currencies": []}


# --- K1-A: the balance card --------------------------------------------------------------------------------------------------

def test_the_balance_card_does_not_repeat_its_figure_in_a_sentence():
    card = cards_for("get_accounts", {}, MURAT_ACCOUNTS)[0]
    assert (card.headline.text, card.sentence) == ("644.659,42 TL", "")


# --- K2-B: the complete month asked with three months and a row of one's own -------------------------------------------------

def test_a_tapped_month_completes_the_question():
    chat, _, gateway, _ = wealth([advice(advice_row())], [[ADVICE]])

    asked = chat.answer("Bana nasıl bir tasarruf önerisi verebilir misin", TOKEN, DAY)
    chat.answer("Ağustos 2026", TOKEN, DAY)

    assert asked.choices == rows("Eylül 2026", "Ağustos 2026", "Temmuz 2026")
    assert gateway.calls[0][:2] == ("get_spending_advice", {"period": "Custom", "from": "2026-08-01", "to": "2026-08-31"})


@pytest.mark.parametrize("reply, arguments", [("2", {"period": "Custom", "from": "2026-08-01", "to": "2026-08-31"}),
                                              ("1", {"period": "LastMonth"}), ("geçen ay", {"period": "LastMonth"}),
                                              ("Haziran", {"period": "Custom", "from": "2026-06-01", "to": "2026-06-30"})])
def test_a_number_a_name_or_last_month_answers_it_too(reply, arguments):
    chat, _, gateway, _ = wealth([income(income_row())], [[INCOME_SHARE]])

    chat.answer("Maaşımın ne kadarını harcadım?", TOKEN, DAY)
    chat.answer(reply, TOKEN, DAY)

    assert gateway.calls[0][:2] == ("get_income_share", arguments)


def test_the_month_question_is_the_fixed_text():
    # The model would word it, and its wording listed periods the tool cannot take ("son 3 ay"); the rows are the months.
    from clarification import ask_complete_month
    assert not ask_complete_month("get_spending_advice", {}, "Tasarruf önerin var mı?", today=DAY.today).model_worded


# --- K4-C: a guessed category -------------------------------------------------------------------------------------------------

def listed(result):
    fake = FakeGateway(result)
    fake.list_tools = lambda token, deadline: [*test_chat.DEFINITIONS, CATEGORY_DETAIL]
    return fake


def test_a_category_the_user_did_not_name_makes_it_a_spending_total():
    # "alışveriş" is all purchases; the 4b model wrote GROCERY, and the check asked "Kaç aylık bakayım?" for it.
    chat, _, gateway, _ = service([ToolCall("get_category_spending", {"categoryCode": "GROCERY", "period": "ThisMonth"})],
                                  gateway=listed(SUMMARY))

    chat.answer("Mart ayında alışverişe ne kadar harcadım", TOKEN, DAY)

    assert gateway.calls[0][:2] == ("get_spending_summary", MARCH)


def test_a_named_category_stays():
    chat, _, gateway, _ = service([ToolCall("get_category_spending", {"categoryCode": "GROCERY", "period": "ThisMonth"})],
                                  gateway=listed(SUMMARY))
    try:
        chat.answer("Mart ayında markete ne kadar harcadım", TOKEN, DAY)
    except KeyError:
        pass   # the fake summary is no category result; only the call that reached the gateway matters
    assert gateway.calls[0][:2] == ("get_category_spending", {"categoryCode": "GROCERY", **MARCH})


def test_no_argument_but_a_count_is_asked_as_a_count():
    # The guard behind K4-C: an argument that is no count gets no "Kaç aylık bakayım?" (None: the fixed clarification instead).
    from chat import ChatService
    schema = {"properties": {"categoryCode": {"type": "string"}, "months": {"type": "integer", "minimum": 1, "maximum": 12}}}
    assert ChatService._question_for(ToolCall("x", {}), ("categoryCode",), schema, "soru") is None
    assert ChatService._question_for(ToolCall("x", {}), ("months",), schema, "soru").kind == "count"


# --- K5-C: a reply that misses its question -----------------------------------------------------------------------------------

def test_a_period_under_a_question_it_does_not_answer_completes_the_question_it_was_asked_for():
    trend = {"months": [], "merchantMatch": None, "currencies": []}
    chat, selector, gateway, _ = service([ToolCall("get_monthly_trend", {"months": 6})], gateway=FakeGateway(trend))
    chat.history.remember(TOKEN, LastCall("get_spending_advice", {"period": "LastMonth"}, "Tasarruf önerin var mı?"))

    asked = chat.answer("Aylık harcamalarım nasıl?", TOKEN, DAY)
    chat.answer("geçen mart ayında", TOKEN, DAY)

    # The reply was read with the question it answered, not with the advice before it (the trial's income share).
    assert asked.answer == "Kaç aylık bakayım?"
    assert selector.calls[-1][0].startswith("Aylık harcamalarım nasıl?")
    assert selector.contexts[-1] is None


def test_an_older_call_is_no_context_after_a_question_was_asked():
    trend = {"months": [], "merchantMatch": None, "currencies": []}
    chat, selector, _, _ = service([ToolCall("get_monthly_trend", {"months": 6})], gateway=FakeGateway(trend))
    chat.history.remember(TOKEN, LastCall("get_income_share", MARCH, "Geçen mart ayında"))

    assert chat.answer("Aylık harcamalarım nasıl?", TOKEN, DAY).answer == "Kaç aylık bakayım?"
    chat.answer("Peki markete?", TOKEN, DAY)      # misses it: a new question, not the income share's follow-up

    assert selector.contexts[-1] is None


# --- "nerelere" ---------------------------------------------------------------------------------------------------------------

@pytest.mark.parametrize("text", ["Peki nerelere harcadım bunu", "Paramı nerelere harcadım?", "Nereler pahalı?"])
def test_nerelere_is_a_word_of_its_own(text):
    assert correct(text) == text


def test_nerelere_is_a_spending_word():
    assert Router(None).route("Peki nerelere harcadım bunu", "get_income_share", 30.0) == Route((GENERAL,))
    # Only "nerelere" says it here.
    assert Router(None).route("Bu ay param nerelere?", None, 30.0) == Route((GENERAL,))


def test_nerelerde_is_still_put_right():
    assert correct("Geçen ay nerelerd tasarruf ederim?") == "Geçen ay nerelerde tasarruf ederim?"
