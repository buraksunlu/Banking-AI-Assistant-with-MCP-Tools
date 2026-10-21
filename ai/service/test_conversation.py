import pytest

from conversation import ConversationStore, LastCall, is_follow_up
from prompts import TOOL_PROMPT_VERSION, TOOL_SYSTEM_PROMPT, build_tool_messages

# CHAT-02: the last successful call is remembered for ten minutes (K2-A) and only short or "peki/ya…" messages are read
# as follow-ups (K4-B). Expected values are written by hand.

TOKEN = "secret-token-123"
STARBUCKS = LastCall("get_merchant_spending", {"merchant": "Starbucks", "period": "LastMonth"},
                     "Geçen ay Starbucks'a ne kadar harcadım?")


class FakeClock:
    def __init__(self):
        self.now = 1000.0

    def __call__(self):
        return self.now


def test_the_last_call_is_remembered_and_can_be_read_again():
    store = ConversationStore()
    store.remember(TOKEN, STARBUCKS)
    assert store.recall(TOKEN) == STARBUCKS
    assert store.recall(TOKEN) == STARBUCKS  # unlike a waiting question, it stays for the next follow-up


def test_a_newer_call_replaces_it():
    store = ConversationStore()
    store.remember(TOKEN, STARBUCKS)
    newer = LastCall("get_spending_summary", {"period": "ThisMonth"}, "Bu ay ne kadar harcadım?")
    store.remember(TOKEN, newer)
    assert store.recall(TOKEN) == newer


def test_it_is_forgotten_ten_minutes_after_the_last_call():
    clock = FakeClock()
    store = ConversationStore(clock=clock)
    store.remember(TOKEN, STARBUCKS)
    clock.now += 599
    assert store.recall(TOKEN) == STARBUCKS
    clock.now += 2
    assert store.recall(TOKEN) is None


def test_another_token_never_sees_it_and_the_token_itself_is_not_kept():
    store = ConversationStore()
    store.remember(TOKEN, STARBUCKS)
    assert store.recall("another-token") is None
    assert TOKEN not in repr(vars(store)) + repr(vars(store._memory))


def test_the_store_has_an_upper_bound():
    store = ConversationStore(capacity=2)
    for token in ("a", "b", "c"):
        store.remember(token, STARBUCKS)
    assert (store.recall("a"), store.recall("b"), store.recall("c")) == (None, STARBUCKS, STARBUCKS)


@pytest.mark.parametrize("message", [
    "Peki geçen ay?", "Ya son 3 ayda?", "Peki Kahve Durağı'na?", "Peki işyerlerine göre?", "Bir önceki ayla karşılaştır",
    "Geçen hafta?", "6 aylık göster", "Aboneliklerim neler?",
    "Ayrıca Kahve Durağı için de aynısını göster lütfen",  # long, but it starts like a follow-up
    "PEKİ BU YIL?",
])
def test_short_or_peki_messages_are_read_as_follow_ups(message):
    assert is_follow_up(message)


@pytest.mark.parametrize("message", [
    "Bu ay ne kadar harcadım?", "Geçen ay Starbucks'a ne kadar harcadım?", "Son 6 ayda harcamam aylara göre nasıl değişti?",
    "Geçen ay en çok hangi kategoriye harcadım?", "",
])
def test_full_questions_stand_on_their_own(message):
    assert not is_follow_up(message)


def test_without_context_the_tool_messages_are_the_same_as_before():
    assert build_tool_messages("Bu ay ne kadar harcadım?") == [
        {"role": "system", "content": TOOL_SYSTEM_PROMPT}, {"role": "user", "content": "Bu ay ne kadar harcadım?"}]


def test_a_follow_up_gets_the_previous_question_and_call():
    previous = LastCall("get_merchant_spending", {"merchant": "Star<bucks>", "period": "LastMonth"}, "Geçen ay <Starbucks>?")

    system, context, user = build_tool_messages("Peki Kahve Durağı'na?", previous)

    assert TOOL_PROMPT_VERSION == "tool-selection-v2"
    assert (system["content"], context["role"], user) == (
        TOOL_SYSTEM_PROMPT, "system", {"role": "user", "content": "Peki Kahve Durağı'na?"})
    assert "Önceki soru: Geçen ay ‹Starbucks›?" in context["content"]
    assert "get_merchant_spending(merchant=Star‹bucks›, period=LastMonth)" in context["content"]
    assert "<" not in context["content"]
