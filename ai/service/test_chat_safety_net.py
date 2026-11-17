"""CHAT-12 PR2: the safety net for free phrasings. Decisions K3-B (intent-v2 knows the account questions and may say "unclear"),
K4-A (a weak word such as "ne kadar" alone decides nothing: the model is asked), K5-A (when it is unclear, a tappable topic
question instead of a guess). Measured in free_eval.py --model; here the behaviour by hand, without a model."""

from datetime import date

import pytest

from clarification import AREAS, ask_area, choices, chosen_tool, template
from chat import ToolCall
from dates import CustomerDay
from prompts import INTENT_PROMPT_VERSION, INTENT_SCHEMA, INTENT_SYSTEM_PROMPT
from routing import ACCOUNTS, GENERAL, PLANNING, Route, Router
from test_chat import TOKEN, service
from test_chat_accounts import AccountGateway
from test_facts_accounts import MURAT_ACCOUNTS

DAY = CustomerDay(date(2026, 10, 10), "Europe/Istanbul")
AREA_TEXT = "Sorunuzu tam anlayamadım. Hangi konuda yardımcı olayım?"


class FakeClassifier:
    def __init__(self, answer):
        self.answer, self.calls = answer, []

    def classify(self, message, deadline):
        self.calls.append(message)
        return self.answer


def route(message, answer="spending", previous=None):
    classifier = FakeClassifier(answer)
    return Router(classifier, clock=lambda: 0.0).route(message, previous, 30.0), classifier


# --- K4-A: a weak word alone asks the model -----------------------------------------------------------------------------------

# CHAT-12 PR3 gave "elimde/kartımda ne kadar var" and "kalan para" their balance rule; these still have none.
@pytest.mark.parametrize("message", ["cüzdanımda ne kadar var", "ne kadar var acaba", "Ziraat'te ne kadar duruyor",
                                     "Migros'a ne kadar?"])
def test_ne_kadar_alone_asks_the_model(message):
    result, classifier = route(message, answer="accounts")
    assert (result, classifier.calls) == (Route((ACCOUNTS,), source="model"), [message])


@pytest.mark.parametrize("message", ["Bu ay ne kadar harcadım?", "Starbucks'a ne kadar verdim?", "Netflix'e ne kadar ödedim?",
                                     "Bu ay markete ne kadar gitti?"])
def test_a_strong_spending_word_still_decides_without_the_model(message):
    result, classifier = route(message, answer="accounts")
    assert (result, classifier.calls) == (Route((GENERAL,)), [])


def test_a_follow_up_with_ne_kadar_keeps_the_last_calls_group():
    result, classifier = route("Peki ne kadar?", previous="check_budget")
    assert (result, classifier.calls) == (Route((PLANNING,), source="previous"), [])


def test_without_a_model_the_weak_word_keeps_the_old_way():
    # The model is down or absent (tests, CHAT-01 K3-A): the general group, as before PR2.
    assert Router(None).route("cüzdanımda ne kadar var", None, 30.0) == Route((GENERAL,), source="fallback")


# --- K3-B: intent-v2 ----------------------------------------------------------------------------------------------------------

def test_intent_v2_knows_the_account_questions_and_unclear():
    assert INTENT_PROMPT_VERSION == "intent-v2"
    assert INTENT_SCHEMA["properties"]["intent"]["enum"] == ["spending", "payments", "planning", "accounts", "unclear"]
    for word in ("accounts:", "bakiye", "maaş", "transfer", "unclear:"):
        assert word in INTENT_SYSTEM_PROMPT


def test_the_model_s_accounts_answer_shows_the_account_tools():
    assert route("durumum nasıl", answer="accounts")[0] == Route((ACCOUNTS,), source="model")


def test_unclear_asks_the_topic():
    assert route("durumum nasıl", answer="unclear")[0] == Route((), source="model", ask_area=True)


# --- K5-A: the topic question -------------------------------------------------------------------------------------------------

def test_the_topic_question_is_fixed_and_its_rows_name_three_groups():
    pending = ask_area("durumum nasıl")
    assert (template(pending), pending.model_worded) == (AREA_TEXT, False)
    assert [(c.label, c.detail) for c in choices(pending)] == [
        ("Harcamalarım", "toplam harcama, kategoriler, işyerleri ve ödemeler"),
        ("Hesabım ve para hareketlerim", "bakiye, maaş ve gelirler, gönderilen ve gelen para"),
        ("Bütçe ve tasarruf", "bütçe kontrolü ya da bir harcamayı azaltınca biriktireceğiniz"),
    ]


@pytest.mark.parametrize("reply, group", [
    ("Harcamalarım", GENERAL), ("Hesabım ve para hareketlerim", ACCOUNTS), ("Bütçe ve tasarruf", PLANNING),
    ("1", GENERAL), ("2", ACCOUNTS), ("bakiye", ACCOUNTS), ("harcama", GENERAL), ("tasarruf", PLANNING),
])
def test_each_row_and_its_words_name_their_group(reply, group):
    assert chosen_tool(ask_area("x"), reply) == group


def test_every_area_value_is_a_router_group():
    assert {area.value for area in AREAS} == {GENERAL, ACCOUNTS, PLANNING}


def test_an_unclear_message_is_asked_and_the_tap_reruns_it_with_that_group():
    chat, selector, gateway, _ = service([], gateway=AccountGateway([MURAT_ACCOUNTS]), turns=[[ToolCall("get_accounts", {})]])
    chat.router = Router(FakeClassifier("unclear"), clock=lambda: 0.0)

    asked = chat.answer("durumum nasıl", TOKEN, DAY)
    reply = chat.answer("Hesabım ve para hareketlerim", TOKEN, DAY)

    assert (asked.type, asked.answer, [c.label for c in asked.choices]) == (
        "clarification", AREA_TEXT, ["Harcamalarım", "Hesabım ve para hareketlerim", "Bütçe ve tasarruf"])
    assert selector.calls[0][0] == "durumum nasıl (Hesabım ve para hareketlerim)"
    assert [t["function"]["name"] for t in selector.calls[0][1]] == ["get_accounts", "search_transactions", "get_income", "get_transfers"]
    assert (reply.type, gateway.calls[0][:2]) == ("answer", ("get_accounts", {}))
