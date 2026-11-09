"""INC-03 PR2: the chat turn of the income share, the saving advice and the two "what if" scenarios.

Decisions: K5-A (advice or a forecast is refused with a fixed text), K6-A (the investment warning is code's), K7-A (an answer that
sounds like advice is replaced by the facts), K8-A/K17-B (the monthly amount and its lira currency are the user's own), K9-A
(fromAdvice only when asked for), K15-A (a complete month), K18-C (the backend's advice sentences are appended by code).
"""

from datetime import date
from decimal import Decimal

import pytest

from chat import ChatReply, ToolCall
from conversation import LastCall
from dates import CustomerDay
from facts import NO_ADVICE_AMOUNT
from prompts import ADVICE_DECLINED_TEXT, LIRA_ONLY_TEXT
from suggestions import OUT_OF_SCOPE_SUGGESTIONS
from test_chat import ANSWER, DEFINITIONS, TOKEN, FakeExplainer, FakeGateway, service
from test_facts_wealth import ADVICE_SENTENCE, DISCLAIMER, GOLD, THREE, advice, advice_row, income, income_row, monthly, one_off

DAY = CustomerDay(date(2026, 10, 10), "Europe/Istanbul")
DATE = {"type": ["string", "null"]}
MONTH = {"type": "string", "enum": ["LastMonth", "Custom"]}
PERIOD = DEFINITIONS[0]["function"]["parameters"]["properties"]["period"]

# The four tools as the server publishes them (PR1): the complete-month ones offer LastMonth and Custom only.
WEALTH_DEFINITIONS = [
    {"type": "function", "function": {"name": name, "description": "Açıklama.", "parameters": {
        "type": "object", "properties": {"period": MONTH, "from": DATE, "to": DATE}, "required": ["period"]}}}
    for name in ("get_income_share", "get_spending_advice")] + [
    {"type": "function", "function": {"name": "what_if_one_off", "description": "Açıklama.", "parameters": {
        "type": "object", "properties": {"period": PERIOD, "categoryCode": {"type": ["string", "null"]}, "from": DATE, "to": DATE},
        "required": ["period"]}}},
    {"type": "function", "function": {"name": "what_if_monthly", "description": "Açıklama.", "parameters": {
        "type": "object", "properties": {"amount": {"type": ["number", "null"]}, "currencyCode": {"type": ["string", "null"], "enum": ["TRY", None]},
                                         "fromAdvice": {"type": "boolean"}}}}},
]


class WealthGateway(FakeGateway):
    def list_tools(self, token, deadline):
        super().list_tools(token, deadline)
        return DEFINITIONS + WEALTH_DEFINITIONS


def wealth(results, turns, **options):
    """A chat whose selector answers turn by turn (``turns``) and whose backend answers call by call (``results``)."""
    return service([], gateway=WealthGateway(list(results)), turns=turns, **options)


INCOME_SHARE = ToolCall("get_income_share", {"period": "LastMonth"})
ADVICE = ToolCall("get_spending_advice", {"period": "LastMonth"})
MONTHLY_1000 = ToolCall("what_if_monthly", {"amount": 1000, "currencyCode": "TRY"})
FROM_ADVICE = ToolCall("what_if_monthly", {"fromAdvice": True})


# --- Income share and advice ----------------------------------------------------------------------------------------------------

def test_the_income_share_is_run_and_explained():
    chat, selector, gateway, explainer = wealth([income(income_row())], [[INCOME_SHARE]])

    reply = chat.answer("Geçen ay maaşımın ne kadarını harcadım?", TOKEN, DAY)

    assert (reply.type, reply.answer, reply.tool_calls) == ("answer", ANSWER, [INCOME_SHARE])
    assert gateway.calls[0][:2] == ("get_income_share", {"period": "LastMonth"})
    # The wealth group only: not the nine spending tools.
    assert [t["function"]["name"] for t in selector.calls[0][1]] == ["get_income_share", "get_spending_advice", "what_if_one_off", "what_if_monthly"]


def test_the_advice_sentences_are_put_under_the_answer_by_code():
    chat, _, _, explainer = wealth([advice(advice_row())], [[ADVICE]])

    reply = chat.answer("Geçen ay nerede tasarruf edebilirim?", TOKEN, DAY)

    # The model saw the groups and "1 öneri aşağıda"; the sentence is the backend's, not retyped.
    assert reply.answer == f"{ANSWER}\n\n{ADVICE_SENTENCE}"
    assert not any(ADVICE_SENTENCE in fact.text for fact in explainer.calls[0][1])


@pytest.mark.parametrize("message", ["Bu ay maaşımın ne kadarını harcadım?", "Son 3 ayda gelirimin ne kadarını harcadım?",
                                     "Maaşımın ne kadarını harcadım?"])
def test_a_period_that_is_not_a_complete_month_is_asked_and_the_tool_does_not_run(message):
    # The model can only write LastMonth: without the question "bu ay" was answered with September, silently.
    chat, _, gateway, _ = wealth([income(income_row())], [[INCOME_SHARE]])

    reply = chat.answer(message, TOKEN, DAY)

    assert reply.type == "clarification" and gateway.calls == []
    assert "tamamlanmış bir ay" in reply.answer and "1) geçen ay (Eylül)" in reply.answer and "Ağustos" in reply.answer


def test_the_reply_last_month_completes_the_question():
    chat, _, gateway, _ = wealth([income(income_row())], [[INCOME_SHARE]])
    chat.answer("Bu ay maaşımın ne kadarını harcadım?", TOKEN, DAY)

    reply = chat.answer("geçen ay", TOKEN, DAY)

    assert reply.type == "answer" and gateway.calls[0][:2] == ("get_income_share", {"period": "LastMonth"})


def test_a_month_name_in_the_reply_runs_that_whole_month():
    chat, _, gateway, _ = wealth([income(income_row())], [[INCOME_SHARE]])
    chat.answer("Bu ay maaşımın ne kadarını harcadım?", TOKEN, DAY)

    chat.answer("Ağustos", TOKEN, DAY)

    assert gateway.calls[0][:2] == ("get_income_share", {"period": "Custom", "from": "2026-08-01", "to": "2026-08-31"})


@pytest.mark.parametrize(("message", "ran"), [("Eylül'de maaşımın ne kadarını harcadım?", True),
                                              ("1-5 Eylül arası maaşımın ne kadarını harcadım?", False),
                                              ("Ekim'de maaşımın ne kadarını harcadım?", False)])
def test_a_named_month_runs_only_when_it_is_whole_and_over(message, ran):
    chat, _, gateway, _ = wealth([income(income_row())], [[INCOME_SHARE]])

    reply = chat.answer(message, TOKEN, DAY)

    assert (len(gateway.calls) == 1) is ran and (reply.type == "answer") is ran
    if ran:
        assert gateway.calls[0][1] == {"period": "Custom", "from": "2026-09-01", "to": "2026-09-30"}
    else:
        assert "tamamlanmış bir ay" in reply.answer


def test_a_follow_up_without_a_period_keeps_the_last_calls_month():
    chat, _, gateway, _ = wealth([income(income_row()), advice(advice_row())], [[INCOME_SHARE], [ADVICE]])
    chat.answer("Geçen ay maaşımın ne kadarını harcadım?", TOKEN, DAY)

    reply = chat.answer("Peki nerede tasarruf edebilirim?", TOKEN, DAY)

    assert reply.type == "answer" and gateway.calls[1][:2] == ("get_spending_advice", {"period": "LastMonth"})


# --- The "what if" scenarios ----------------------------------------------------------------------------------------------------

def test_the_one_off_scenario_gets_the_warning_under_the_answer():
    call = ToolCall("what_if_one_off", {"period": "LastMonth", "categoryCode": "ENTERTAINMENT"})
    chat, _, _, _ = wealth([one_off(THREE)], [[call]])

    reply = chat.answer("Geçen ay eğlence harcamamı dolar alsaydım ne olurdu?", TOKEN, DAY)

    assert reply.answer == f"{ANSWER}\n\n{DISCLAIMER}" and reply.tool_calls == [call]


def test_a_monthly_amount_the_user_wrote_runs_with_its_lira_currency():
    chat, _, gateway, _ = wealth([monthly("User", Decimal("1000.00"), assets=[GOLD])], [[MONTHLY_1000]])

    reply = chat.answer("Ayda 1.000 TL altın olarak biriktirseydim ne olurdu?", TOKEN, DAY)

    assert gateway.calls[0][:2] == ("what_if_monthly", {"amount": 1000, "currencyCode": "TRY"})
    assert reply.answer.endswith(DISCLAIMER)


def test_the_amount_is_asked_when_the_user_did_not_write_it_and_the_model_wrote_one():
    chat, _, gateway, _ = wealth([monthly("User", Decimal("1000.00"), assets=[GOLD])], [[MONTHLY_1000]])

    asked = chat.answer("Ayda biriktirseydim ne olurdu?", TOKEN, DAY)
    done = chat.answer("1.000 TL", TOKEN, DAY)

    assert asked == ChatReply("clarification", "Ayda kaç TL biriktirirdiniz? Örneğin 1.000.", [], [])
    assert done.type == "answer" and gateway.calls[0][:2] == ("what_if_monthly", {"amount": 1000, "currencyCode": "TRY"})


@pytest.mark.parametrize("reply", ["1000", "1.000", "bin", "1000 TL", "1 bin lira"])
def test_a_bare_amount_in_the_reply_runs_as_lira_without_a_currency_question(reply):
    # CHAT-09 K6-B: the simulator asked "TL, Euro, Dolar or Sterlin?" after "5000" although only lira can run.
    chat, _, gateway, _ = wealth([monthly("User", Decimal("1000.00"), assets=[GOLD])], [[MONTHLY_1000]])
    chat.answer("Ayda biriktirseydim ne olurdu?", TOKEN, DAY)

    done = chat.answer(reply, TOKEN, DAY)

    assert done.type == "answer" and gateway.calls[0][1] == {"amount": 1000, "currencyCode": "TRY"}


@pytest.mark.parametrize("message", ["Ayda 100 dolar biriktirseydim ne olurdu?"])
def test_an_amount_in_another_currency_is_a_fixed_answer(message):
    chat, _, gateway, explainer = wealth([], [[ToolCall("what_if_monthly", {"amount": 100, "currencyCode": "TRY"})]])

    reply = chat.answer(message, TOKEN, DAY)

    assert reply == ChatReply("answer", LIRA_ONLY_TEXT, [], [], []) and gateway.calls == [] and explainer.calls == []


def test_the_currency_chosen_in_the_reply_is_checked_too():
    chat, _, gateway, _ = wealth([], [[MONTHLY_1000]])
    chat.answer("Ayda biriktirseydim ne olurdu?", TOKEN, DAY)

    reply = chat.answer("100 dolar", TOKEN, DAY)

    assert reply.answer == LIRA_ONLY_TEXT and gateway.calls == []


def test_the_advised_amount_follows_an_advice_answer():
    chat, _, gateway, _ = wealth([advice(advice_row()), monthly(assets=[GOLD])], [[ADVICE], [FROM_ADVICE]])
    chat.answer("Geçen ay nerede tasarruf edebilirim?", TOKEN, DAY)

    reply = chat.answer("Peki önerdiğin tutarı biriktirseydim?", TOKEN, DAY)

    assert reply.type == "answer" and gateway.calls[1][:2] == ("what_if_monthly", {"fromAdvice": True})


def test_the_advised_amount_runs_when_the_user_asks_for_it_in_words():
    chat, _, gateway, _ = wealth([monthly(assets=[GOLD])], [[FROM_ADVICE]])

    chat.answer("Önerdiğin tutarı her ay biriktirseydim ne olurdu?", TOKEN, DAY)

    assert gateway.calls[0][:2] == ("what_if_monthly", {"fromAdvice": True})


def test_the_advised_amount_the_user_did_not_ask_for_is_not_run():
    chat, _, gateway, _ = wealth([], [[FROM_ADVICE]])

    reply = chat.answer("Ayda biriktirseydim ne olurdu?", TOKEN, DAY)

    assert reply.type == "clarification" and gateway.calls == []


def test_no_advice_amount_is_a_fixed_answer_without_the_model():
    chat, _, _, explainer = wealth([monthly(amount=None, status="NoAdvice")], [[FROM_ADVICE]])

    reply = chat.answer("Önerdiğin tutarı biriktirseydim ne olurdu?", TOKEN, DAY)

    assert (reply.type, reply.answer) == ("answer", NO_ADVICE_AMOUNT) and explainer.calls == []


# --- K5-A, K7-A -----------------------------------------------------------------------------------------------------------------

@pytest.mark.parametrize("message", ["Altın alayım mı?", "Dolar yükselir mi?", "Bana yatırım tavsiyesi verir misin?"])
def test_advice_or_a_forecast_is_refused_with_a_fixed_text_and_no_model(message):
    chat, selector, gateway, explainer = wealth([], [[]])

    reply = chat.answer(message, TOKEN, DAY)

    assert reply == ChatReply("outOfScope", ADVICE_DECLINED_TEXT, [], [], list(OUT_OF_SCOPE_SUGGESTIONS))
    assert selector.calls == [] and gateway.calls == [] and explainer.calls == []


@pytest.mark.parametrize("sentence", ["Altın almalısınız, çok kazandırır.", "Dolar yükselecek, tavsiye ederim.",
                                      "Euro düşecek gibi görünüyor."])
def test_an_explanation_that_sounds_like_advice_is_replaced_by_the_facts_and_the_warning(sentence):
    chat, _, _, _ = wealth([one_off(THREE)], [[ToolCall("what_if_one_off", {"period": "LastMonth"})]], explainer=FakeExplainer(sentence))

    reply = chat.answer("Geçen ay harcadığım parayla altın alsaydım ne olurdu?", TOKEN, DAY)

    assert sentence not in reply.answer and reply.answer.endswith(DISCLAIMER)
    assert "Dolar alsaydınız: alışta 41,15 TL" in reply.answer and "Gram altın alsaydınız: alışta 4.120,1235 TL" in reply.answer


# --- K19-B: the questions offered under the answers -----------------------------------------------------------------------------

def test_the_income_answer_offers_the_advice_and_the_advice_offers_the_saving():
    chat, selector, gateway, _ = wealth([income(income_row()), advice(advice_row()), monthly(assets=[GOLD])],
                                        [[INCOME_SHARE]])

    first = chat.answer("Geçen ay maaşımın ne kadarını harcadım?", TOKEN, DAY)
    second = chat.answer("Peki nerede tasarruf edebilirim?", TOKEN, DAY)
    third = chat.answer("Peki önerdiğin tutarı biriktirseydim?", TOKEN, DAY)

    assert first.suggestions == ["Peki nerede tasarruf edebilirim?"]
    assert second.suggestions == ["Peki önerdiğin tutarı biriktirseydim?", "Geçen ay maaşımın ne kadarını harcadım?"]
    # The offered texts ran their calls without a second tool selection.
    assert len(selector.calls) == 1 and [c[0] for c in gateway.calls] == ["get_income_share", "get_spending_advice", "what_if_monthly"]
    assert third.suggestions == [] and gateway.calls[2][1] == {"fromAdvice": True}


@pytest.mark.parametrize("message", ["Önerdiğin tutarı her ay biriktirseydim ne olurdu?"])
def test_the_advised_saving_runs_even_when_the_model_chooses_no_tool(message):
    # Measured W12/W13: the small model answers in text. The words name the call, so code runs it and the model is not asked.
    chat, selector, gateway, _ = wealth([monthly(assets=[GOLD])], [[]])

    reply = chat.answer(message, TOKEN, DAY)

    assert reply.type == "answer" and gateway.calls[0][:2] == ("what_if_monthly", {"fromAdvice": True})
    assert selector.calls == []


# --- CHAT-09 PR2: the scenario the words chose (K5-D) and a model that chooses nothing there (K13-A) --------------------------

def test_the_model_is_shown_only_the_scenario_the_words_chose():
    call = ToolCall("what_if_one_off", {"period": "LastMonth"})
    chat, selector, gateway, _ = wealth([one_off(THREE)], [[call]])

    chat.answer("Geçen ayki harcamalarımı biriktirseydim nolurdu", TOKEN, DAY)

    assert [t["function"]["name"] for t in selector.calls[0][1]] == ["what_if_one_off"]
    assert gateway.calls[0][:2] == ("what_if_one_off", {"period": "LastMonth"})


def test_a_monthly_scenario_the_model_did_not_choose_asks_the_amount():
    # The model run of PR1: "AYDA BİRİKTİRSEYDİM NE OLURDU?" got the out-of-scope text with the monthly tool alone on the list.
    chat, _, gateway, _ = wealth([], [[]])

    reply = chat.answer("AYDA BİRİKTİRSEYDİM NE OLURDU?", TOKEN, DAY)

    assert reply == ChatReply("clarification", "Ayda kaç TL biriktirirdiniz? Örneğin 1.000.", [], []) and gateway.calls == []


def test_a_monthly_scenario_with_its_amount_runs_when_the_model_chose_nothing():
    chat, _, gateway, _ = wealth([monthly("User", Decimal("1000.00"), assets=[GOLD])], [[]])

    chat.answer("Her ay 1.000 TL biriktirseydim ne olurdu?", TOKEN, DAY)

    assert gateway.calls[0][:2] == ("what_if_monthly", {"amount": 1000, "currencyCode": "TRY"})


def test_a_one_off_scenario_the_model_did_not_choose_runs_the_period_the_user_named():
    chat, _, gateway, _ = wealth([one_off(THREE)], [[]])

    chat.answer("Geçen ay harcadığım parayla altın alsaydım ne olurdu?", TOKEN, DAY)

    assert gateway.calls[0][:2] == ("what_if_one_off", {"period": "LastMonth"})


def test_a_one_off_scenario_without_a_period_asks_it_when_the_model_chose_nothing():
    chat, _, gateway, _ = wealth([one_off(THREE)], [[], []])

    asked = chat.answer("Harcadığım parayla altın alsaydım ne olurdu?", TOKEN, DAY)
    chat.answer("geçen hafta", TOKEN, DAY)

    assert asked.type == "clarification" and "Hangi dönem için?" in asked.answer
    assert gateway.calls[0][:2] == ("what_if_one_off", {"period": "LastWeek"})


def test_the_category_waits_with_the_period_question():
    chat, _, gateway, _ = wealth([one_off(THREE)], [[], []])

    chat.answer("Eğlence harcamamı dolar alsaydım ne olurdu?", TOKEN, DAY)
    chat.answer("geçen ay", TOKEN, DAY)

    assert gateway.calls[0][:2] == ("what_if_one_off", {"categoryCode": "ENTERTAINMENT", "period": "LastMonth"})


def test_the_explainer_answers_the_question_code_wrote_for_a_what_if():
    # K7-A: the user's question went through two questions back ("(5000) (Tl)"); the explainer gets the scenario instead.
    chat, _, _, explainer = wealth([monthly("User", Decimal("5000.00"), assets=[GOLD])], [[]])
    chat.answer("Ayda biriktirseydim ne olurdu?", TOKEN, DAY)

    chat.answer("5000", TOKEN, DAY)

    assert explainer.calls[0][0] == "Son 12 ay boyunca her ay 5.000,00 TL ile gram altın alsaydım bugün ne olurdu?"


def test_values_written_next_to_another_assets_name_are_replaced_by_the_facts():
    # K16-A: the simulator's "dolar, euro veya gram altın ... sırasıyla A, B ve C".
    sentence = ("Bu miktar dolar, euro veya gram altın olarak değerlendirilirse değerler sırasıyla 2.297,38 TL, 2.222,62 TL ve "
                "2.194,89 TL olur.")
    call = ToolCall("what_if_one_off", {"period": "LastMonth", "categoryCode": "ENTERTAINMENT"})
    chat, _, _, _ = wealth([one_off(THREE)], [[call]], explainer=FakeExplainer(sentence))

    reply = chat.answer("Geçen ay eğlence harcamamı dolar alsaydım ne olurdu?", TOKEN, DAY)

    assert sentence not in reply.answer
    assert "Dolar alsaydınız: alışta 41,15 TL" in reply.answer and reply.answer.endswith(DISCLAIMER)


def test_a_category_the_user_named_and_the_model_left_out_is_put_in_by_code():
    # Model run of PR2: with the one-off tool shown alone, W07 came back without its category and would have bought all spending.
    chat, _, gateway, _ = wealth([one_off(THREE)], [[ToolCall("what_if_one_off", {"period": "LastMonth"})]])

    chat.answer("Geçen ay eğlence harcamamı dolar alsaydım ne olurdu?", TOKEN, DAY)

    assert gateway.calls[0][:2] == ("what_if_one_off", {"period": "LastMonth", "categoryCode": "ENTERTAINMENT"})
