"""CHAT-09 PR3: a question that fits two calculations is asked back instead of guessed.

Decisions: K17-C, replaced by CHAT-10 PR3b D8-B (12 Nov 2026): an undecided "what if" is the monthly saving, and its amount is
asked; the user found "tek seferlik mi, aylık mı?" an odd first question. K18-B (a period, date or category means the one-off, an
amount the monthly saving), K19-C (a planning question the model chose nothing for: the words choose, else which calculation is
asked), K20-B (the model words the question in the user's words, code checks it).
"""

from decimal import Decimal

import pytest

from chat import ChatReply, ToolCall
from test_chat import (AMOUNT_CHOICES, AMOUNT_HINT, AMOUNT_QUESTION, MONTHLY_CHOICES, PERCENT_CHOICES, TOPIC_CHOICES, BUDGET_RESULT, SAVINGS_RESULT, TOKEN, YESTERDAY, FakeGateway, FakeWriter, service)
from test_chat_wealth import DAY, GOLD, THREE, monthly, one_off, wealth
from test_clarification import TOPIC_TEXT

MONTHLY_AMOUNT_QUESTION = "Ayda kaç TL biriktirirdiniz?"
PERCENT_QUESTION = "Ne kadar azaltmayı düşünüyorsunuz?"


def names(tools):
    return [t["function"]["name"] for t in tools]


# --- PR3b D8-B: an undecided "what if" is the monthly saving -------------------------------------------------------------------

ASKED_AMOUNT = ChatReply("clarification", MONTHLY_AMOUNT_QUESTION, [], [], choices=MONTHLY_CHOICES, input_hint=AMOUNT_HINT)


@pytest.mark.parametrize("message", ["Biriktirseydim ne olurdu?", "Dolar alsaydım ne olurdu?", "Altın olarak tutsaydım ne olurdu?",
                                     "Her ay harcadığım kadar biriktirseydim ne olurdu?"])
def test_an_undecided_what_if_asks_the_monthly_amount_instead_of_the_scenario(message):
    chat, selector, gateway, _ = wealth([], [[]])

    reply = chat.answer(message, TOKEN, DAY)

    assert reply == ASKED_AMOUNT
    # The model is not asked: the monthly saving's amount comes only from the words (INC-03 K8-A), so it is asked.
    assert selector.calls == [] and gateway.calls == []


def test_tapping_an_amount_example_runs_the_monthly_saving():
    chat, _, gateway, _ = wealth([monthly("User", Decimal("5000.00"), assets=[GOLD])], [[]])
    asked = chat.answer("Dolar alsaydım ne olurdu?", TOKEN, DAY)

    duration = chat.answer(asked.choices[1].label, TOKEN, DAY)
    reply = chat.answer(duration.choices[2].label, TOKEN, DAY)

    assert [c.label for c in duration.choices] == ["3 ay", "6 ay", "12 ay"]
    assert reply.type == "answer"
    assert gateway.calls[0][:2] == ("what_if_monthly", {"amount": 5000, "currencyCode": "TRY", "months": 12})


def test_a_typed_amount_of_ones_own_runs_too():
    chat, _, gateway, _ = wealth([monthly("User", Decimal("2000.00"), assets=[GOLD])], [[]])
    chat.answer("Altın olarak tutsaydım ne olurdu?", TOKEN, DAY)

    chat.answer("her ay 2 bin", TOKEN, DAY)
    chat.answer("8", TOKEN, DAY)

    assert gateway.calls[0][:2] == ("what_if_monthly", {"amount": 2000, "currencyCode": "TRY", "months": 8})


@pytest.mark.parametrize("reply", ["tek seferlik", "bir kerede", "toplu"])
def test_one_off_words_under_the_amount_question_switch_to_the_one_off(reply):
    # D8-B keeps a way to the one-off: the user who meant "at once" says so, and its period is asked as before.
    chat, _, gateway, _ = wealth([one_off(THREE)], [[], []])
    chat.answer("Dolar alsaydım ne olurdu?", TOKEN, DAY)

    asked = chat.answer(reply, TOKEN, DAY)
    chat.answer("geçen ay", TOKEN, DAY)

    assert asked.type == "clarification" and "Hangi dönem için?" in asked.answer
    assert gateway.calls[0][:2] == ("what_if_one_off", {"period": "LastMonth"})


def test_one_off_words_in_a_longer_message_are_a_new_question():
    chat, _, gateway, _ = wealth([YESTERDAY], [[ToolCall("get_spending_summary", {"period": "Yesterday"})]])
    chat.answer("Dolar alsaydım ne olurdu?", TOKEN, DAY)

    chat.answer("Dün tek seferlik ne kadar harcadım?", TOKEN, DAY)

    assert gateway.calls[0][0] == "get_spending_summary"


def test_a_reply_that_is_not_an_amount_is_a_new_question():
    chat, _, gateway, _ = wealth([YESTERDAY], [[ToolCall("get_spending_summary", {"period": "Yesterday"})]])
    chat.answer("Biriktirseydim ne olurdu?", TOKEN, DAY)

    chat.answer("Dün ne harcadım?", TOKEN, DAY)

    assert gateway.calls[0][:2] == ("get_spending_summary", {"period": "Yesterday"})


# --- K18-B: the figures the user wrote decide ----------------------------------------------------------------------------------

def test_a_period_means_the_one_off_without_a_question():
    call = ToolCall("what_if_one_off", {"period": "LastMonth"})
    chat, selector, gateway, _ = wealth([one_off(THREE)], [[call]])

    chat.answer("Geçen ay altın alsaydım ne olurdu?", TOKEN, DAY)

    assert names(selector.calls[0][1]) == ["what_if_one_off"]
    assert gateway.calls[0][:2] == ("what_if_one_off", {"period": "LastMonth"})


def test_a_category_means_the_one_off():
    chat, selector, _, _ = wealth([one_off(THREE)], [[]])

    chat.answer("Eğlence için dolar alsaydım ne olurdu?", TOKEN, DAY)

    assert names(selector.calls[0][1]) == ["what_if_one_off"]


def test_the_count_of_a_period_is_not_an_amount():
    chat, selector, _, _ = wealth([one_off(THREE)], [[]])

    chat.answer("Son 3 ayda dolar alsaydım ne olurdu?", TOKEN, DAY)

    assert names(selector.calls[0][1]) == ["what_if_one_off"]


def test_an_amount_means_the_monthly_saving():
    chat, selector, gateway, _ = wealth([monthly("User", Decimal("5000.00"), assets=[GOLD])], [[]])

    chat.answer("5.000 TL 6 ay biriktirseydim ne olurdu?", TOKEN, DAY)

    assert names(selector.calls[0][1]) == ["what_if_monthly"]
    assert gateway.calls[0][:2] == ("what_if_monthly", {"amount": 5000, "currencyCode": "TRY", "months": 6})


def test_a_period_and_an_amount_are_the_monthly_saving_of_that_amount():
    # D8-B: figures that point both ways no longer ask; the amount the user said runs (the monthly saving takes no period).
    chat, selector, gateway, _ = wealth([monthly("User", Decimal("5000.00"), assets=[GOLD])], [[]])

    chat.answer("Geçen ay 5.000 TL biriktirseydim ne olurdu?", TOKEN, DAY)
    chat.answer("6 ay", TOKEN, DAY)

    assert selector.calls == []
    assert gateway.calls[0][:2] == ("what_if_monthly", {"amount": 5000, "currencyCode": "TRY", "months": 6})


def test_a_date_means_the_one_off():
    chat, selector, _, _ = wealth([one_off(THREE)], [[]])

    chat.answer("1-5 Eylül'de altın alsaydım ne olurdu?", TOKEN, DAY)

    assert names(selector.calls[0][1]) == ["what_if_one_off"]


def test_a_written_answer_for_a_range_of_days_names_the_days_once():
    # CHAT-08 K16-A puts "Dönem: …" above an explained answer; a code-written one already starts with its dates (PR3b D10-A).
    chat, _, _, _ = wealth([one_off(THREE)], [[]])

    reply = chat.answer("1-5 Eylül'de altın alsaydım ne olurdu?", TOKEN, DAY)

    assert reply.type == "answer" and not reply.answer.startswith("Dönem:")


def test_both_scenarios_words_are_the_monthly_saving_whatever_the_figures_say():
    chat, _, _, _ = wealth([], [[]])

    assert chat.answer("Her ay harcadığım kadar geçen ay biriktirseydim?", TOKEN, DAY) == ASKED_AMOUNT


# --- K19-C: a planning question the model left -------------------------------------------------------------------------------

def test_a_budget_question_the_model_left_asks_the_budget():
    # S40: before PR3 this was the out-of-scope text.
    chat, selector, gateway, _ = service([], gateway=FakeGateway(BUDGET_RESULT))

    reply = chat.answer("Bu ay bütçemi aşacak mıyım?", TOKEN)
    chat.answer("1.500 TL", TOKEN)

    assert reply == ChatReply("clarification", AMOUNT_QUESTION, [], [], choices=AMOUNT_CHOICES, input_hint=AMOUNT_HINT)
    # The model was shown both planning tools, as before (K19-C: its list does not change).
    assert names(selector.calls[0][1]) == ["check_budget", "simulate_savings"]
    assert gateway.calls[0][:2] == ("check_budget", {"amount": 1500, "currencyCode": "TRY"})


def test_the_category_the_user_named_waits_with_the_budget_question():
    chat, _, gateway, _ = service([], gateway=FakeGateway(BUDGET_RESULT))

    chat.answer("Market bütçemi aşar mıyım?", TOKEN)
    chat.answer("2.000 TL", TOKEN)

    assert gateway.calls[0][:2] == ("check_budget", {"amount": 2000, "currencyCode": "TRY", "categoryCode": "GROCERY"})


def test_a_saving_question_the_model_left_asks_the_cut():
    chat, _, gateway, _ = service([], gateway=FakeGateway(SAVINGS_RESULT))

    reply = chat.answer("Tasarruf etsem ne olur?", TOKEN)

    assert reply == ChatReply("clarification", PERCENT_QUESTION, [], [], choices=PERCENT_CHOICES) and gateway.calls == []


def test_words_of_both_ask_which_calculation():
    chat, selector, gateway, _ = service([], gateway=FakeGateway(SAVINGS_RESULT))

    reply = chat.answer("Bütçemi kısıp tasarruf etsem?", TOKEN)
    asked = chat.answer("2", TOKEN)
    chat.answer("%20", TOKEN)

    assert reply == ChatReply("clarification", TOPIC_TEXT, [], [], choices=TOPIC_CHOICES)
    assert asked.answer == PERCENT_QUESTION
    # After the choice the model is shown the chosen tool alone, like a scenario the words chose.
    assert names(selector.calls[1][1]) == ["simulate_savings"]
    assert gateway.calls[0][:2] == ("simulate_savings", {"reductionPercent": 20})


def test_a_planning_choice_the_model_makes_runs_as_before():
    call = ToolCall("check_budget", {"amount": 3000, "currencyCode": "TRY"})
    chat, _, gateway, _ = service([call], gateway=FakeGateway(BUDGET_RESULT))

    chat.answer("Bu ay 3.000 TL bütçemi aşar mıyım?", TOKEN)

    assert gateway.calls[0][:2] == ("check_budget", {"amount": 3000, "currencyCode": "TRY"})


def test_a_wealth_question_that_is_no_what_if_is_not_asked():
    chat, selector, _, _ = wealth([], [[]])

    reply = chat.answer("Geçen ay nerede tasarruf edebilirim?", TOKEN, DAY)

    assert reply.type == "outOfScope" and "get_spending_advice" in names(selector.calls[0][1])


# --- CHAT-10 PR3: tapping a row sends its label (K6-A) -------------------------------------------------------------------------

def test_tapping_a_budget_example_runs_the_budget_without_asking_the_currency():
    chat, _, gateway, _ = service([], gateway=FakeGateway(BUDGET_RESULT))
    asked = chat.answer("Market bütçemi aşar mıyım?", TOKEN)

    reply = chat.answer(asked.choices[0].label, TOKEN)

    assert reply.type == "answer" and reply.choices == []
    assert gateway.calls[0][:2] == ("check_budget", {"amount": 1000, "currencyCode": "TRY", "categoryCode": "GROCERY"})


def test_tapping_a_cut_runs_the_saving():
    chat, _, gateway, _ = service([], gateway=FakeGateway(SAVINGS_RESULT))
    asked = chat.answer("Starbucks harcamamı azaltırsam ne kadar biriktiririm?", TOKEN)

    chat.answer(asked.choices[2].label, TOKEN)

    assert gateway.calls[0][1]["reductionPercent"] == 50
