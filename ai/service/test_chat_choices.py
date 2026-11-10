"""CHAT-09 PR3: a question that fits two calculations is asked back instead of guessed.

Decisions: K17-C (an undecided "what if" asks which scenario, without the model), K18-B (a period, date or category means the
one-off, an amount the monthly saving, both ask), K19-C (a planning question the model chose nothing for: the words choose,
else which calculation is asked), K20-B (the model words the question in the user's words, code checks it).
"""

from decimal import Decimal

from chat import ChatReply, ToolCall
from test_chat import (AMOUNT_QUESTION, BUDGET_RESULT, SAVINGS_RESULT, TOKEN, YESTERDAY, FakeGateway, FakeWriter, service)
from test_chat_wealth import DAY, GOLD, THREE, monthly, one_off, wealth
from test_clarification import SCENARIO_TEXT, TOPIC_TEXT

MONTHLY_AMOUNT_QUESTION = "Ayda kaç TL biriktirirdiniz? Örneğin 1.000."
PERCENT_QUESTION = "Ne kadar azaltmayı düşünüyorsunuz? Örneğin %10, %25 ya da %50."


def names(tools):
    return [t["function"]["name"] for t in tools]


# --- K17-C: which scenario -----------------------------------------------------------------------------------------------------

def test_an_undecided_what_if_is_asked_before_the_model_or_the_server():
    chat, selector, gateway, _ = wealth([], [[ToolCall("what_if_one_off", {"period": "LastMonth"})]])

    reply = chat.answer("Biriktirseydim ne olurdu?", TOKEN, DAY)

    assert reply == ChatReply("clarification", SCENARIO_TEXT, [], [])
    assert selector.calls == [] and gateway.calls == [] and gateway.list_calls == []


def test_both_scenarios_words_ask_even_when_the_model_would_choose():
    # Before PR3 the model's one-off ran for "her ay … harcadığım" (the probe of 10 Nov 2026).
    chat, selector, gateway, _ = wealth([], [[ToolCall("what_if_one_off", {"period": "LastMonth"})]])

    reply = chat.answer("Her ay harcadığım kadar biriktirseydim ne olurdu?", TOKEN, DAY)

    assert reply.answer == SCENARIO_TEXT and selector.calls == [] and gateway.calls == []


def test_the_monthly_choice_asks_the_amount_then_runs():
    chat, selector, gateway, _ = wealth([monthly("User", Decimal("1000.00"), assets=[GOLD])], [[], []])
    chat.answer("Biriktirseydim ne olurdu?", TOKEN, DAY)

    asked = chat.answer("2", TOKEN, DAY)
    chat.answer("1.000", TOKEN, DAY)

    assert asked == ChatReply("clarification", MONTHLY_AMOUNT_QUESTION, [], [])
    # The user's question, without the bare "2" that would read as an amount; the model saw only the chosen tool.
    assert selector.calls[0][0] == "Biriktirseydim ne olurdu?" and names(selector.calls[0][1]) == ["what_if_monthly"]
    assert gateway.calls[0][:2] == ("what_if_monthly", {"amount": 1000, "currencyCode": "TRY"})


def test_the_one_off_choice_asks_the_period_then_runs():
    chat, _, gateway, _ = wealth([one_off(THREE)], [[], []])
    chat.answer("Dolar alsaydım ne olurdu?", TOKEN, DAY)

    asked = chat.answer("tek seferlik", TOKEN, DAY)
    chat.answer("geçen ay", TOKEN, DAY)

    assert asked.type == "clarification" and "Hangi dönem için?" in asked.answer
    assert gateway.calls[0][:2] == ("what_if_one_off", {"period": "LastMonth"})


def test_a_reply_in_words_carries_its_figures():
    chat, selector, gateway, _ = wealth([monthly("User", Decimal("2000.00"), assets=[GOLD])], [[]])
    chat.answer("Altın olarak tutsaydım ne olurdu?", TOKEN, DAY)

    chat.answer("her ay 2 bin", TOKEN, DAY)

    assert selector.calls[0][0] == "Altın olarak tutsaydım ne olurdu? (her ay 2 bin)"
    assert gateway.calls[0][:2] == ("what_if_monthly", {"amount": 2000, "currencyCode": "TRY"})


def test_a_reply_that_is_not_a_choice_is_a_new_question():
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

    chat.answer("5.000 TL biriktirseydim ne olurdu?", TOKEN, DAY)

    assert names(selector.calls[0][1]) == ["what_if_monthly"]
    assert gateway.calls[0][:2] == ("what_if_monthly", {"amount": 5000, "currencyCode": "TRY"})


def test_a_period_and_an_amount_ask():
    chat, selector, _, _ = wealth([], [[]])

    reply = chat.answer("Geçen ay 5.000 TL biriktirseydim ne olurdu?", TOKEN, DAY)

    assert reply.answer == SCENARIO_TEXT and selector.calls == []


def test_a_date_means_the_one_off():
    chat, selector, _, _ = wealth([one_off(THREE)], [[]])

    chat.answer("1-5 Eylül'de altın alsaydım ne olurdu?", TOKEN, DAY)

    assert names(selector.calls[0][1]) == ["what_if_one_off"]


def test_both_scenarios_words_ask_whatever_the_figures_say():
    chat, selector, _, _ = wealth([], [[]])

    reply = chat.answer("Her ay harcadığım kadar geçen ay biriktirseydim?", TOKEN, DAY)

    assert reply.answer == SCENARIO_TEXT and selector.calls == []


# --- K20-B: the model words the question ---------------------------------------------------------------------------------------

def test_the_model_words_the_scenario_question_in_the_users_words():
    text = "Geçen ayki gibi bir kerede mi dolar alsaydınız, yoksa her ay düzenli olarak mı biriktirseydiniz?"
    writer = FakeWriter(text)
    chat, _, _, _ = wealth([], [[]], writer=writer)

    reply = chat.answer("Dolar alsaydım ne olurdu?", TOKEN, DAY)

    assert reply.answer == text
    assert writer.calls[0][:4] == ("Dolar alsaydım ne olurdu?", "scenario", ["tek seferlik", "aylık birikim"], True)


def test_a_worded_question_that_leaves_a_choice_out_is_replaced_by_the_fixed_one():
    chat, _, _, _ = wealth([], [[]], writer=FakeWriter("Bir kerede mi hesaplayayım?"))

    assert chat.answer("Dolar alsaydım ne olurdu?", TOKEN, DAY).answer == SCENARIO_TEXT


# --- K19-C: a planning question the model chose nothing for --------------------------------------------------------------------

def test_a_budget_question_the_model_left_asks_the_budget():
    # S40: before PR3 this was the out-of-scope text.
    chat, selector, gateway, _ = service([], gateway=FakeGateway(BUDGET_RESULT))

    reply = chat.answer("Bu ay bütçemi aşacak mıyım?", TOKEN)
    chat.answer("1.500 TL", TOKEN)

    assert reply == ChatReply("clarification", AMOUNT_QUESTION, [], [])
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

    assert reply == ChatReply("clarification", PERCENT_QUESTION, [], []) and gateway.calls == []


def test_words_of_both_ask_which_calculation():
    chat, selector, gateway, _ = service([], gateway=FakeGateway(SAVINGS_RESULT))

    reply = chat.answer("Bütçemi kısıp tasarruf etsem?", TOKEN)
    asked = chat.answer("2", TOKEN)
    chat.answer("%20", TOKEN)

    assert reply == ChatReply("clarification", TOPIC_TEXT, [], [])
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
