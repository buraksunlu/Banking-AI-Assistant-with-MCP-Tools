"""A brand is a merchant, not a category (fix after the UX-01 PR3 model run, 16 Nov 2026): the model took "Netflix'e ne kadar" for the
category detail of subscriptions, and the argument check let it pass because "netflix" is one of that category's words. Code turns
such a call into the merchant's own (K1-B); a category named in its own word stays a category."""

import pytest

import test_chat
from chat import ToolCall
from test_chat import DATE, PERIOD, TOKEN, FakeGateway, merchant_result, service
from test_cards import category_result, category_row
from test_chat_dates import DAY

CATEGORY_DETAIL = {"type": "function", "function": {
    "name": "get_category_spending", "description": "Tek bir kategorinin bir dönemdeki harcamasını döndürür.",
    "parameters": {"type": "object", "properties": {
        "categoryCode": {"type": "string", "enum": ["GROCERY", "FOOD", "SUBSCRIPTION", "OTHER"]}, "period": PERIOD, "from": DATE,
        "to": DATE}, "required": ["categoryCode", "period"]}}}

# Murat's September Netflix (SQL, 16 Nov 2026): one purchase of 229,99 TL.
NETFLIX_SEPTEMBER = merchant_result("Netflix", "LastMonth", "2026-09-01", "2026-09-30", "229.99", 1)
SUBSCRIPTION_LAST_MONTH = ToolCall("get_category_spending", {"categoryCode": "SUBSCRIPTION", "period": "LastMonth"})


@pytest.fixture(autouse=True)
def category_tool_listed(monkeypatch):
    monkeypatch.setattr(test_chat, "DEFINITIONS", [*test_chat.DEFINITIONS, CATEGORY_DETAIL])


def gateway(result=NETFLIX_SEPTEMBER):
    fake = FakeGateway(result)
    fake.list_tools = lambda token, deadline: test_chat.DEFINITIONS
    return fake


@pytest.mark.parametrize("message", ["Geçen ay Netflix'e ne kadar harcadım?", "Netflix'e gecen ay ne kadar harcadim?",
                                     "Geçen ay Netflix aboneliğime ne kadar ödedim?"])
def test_a_brand_the_model_took_for_its_category_runs_as_the_merchant(message):
    chat, _, fake, _ = service([SUBSCRIPTION_LAST_MONTH], gateway=gateway())

    reply = chat.answer(message, TOKEN)

    assert fake.calls[0][:2] == ("get_merchant_spending", {"merchant": "Netflix", "period": "LastMonth"})
    assert reply.tool_calls == [ToolCall("get_merchant_spending", {"merchant": "Netflix", "period": "LastMonth"})]
    assert reply.type == "answer"


def test_a_brand_without_purchases_says_so_instead_of_the_categorys_change():
    # Can's Netflix had no purchase last month (A10): the answer was "Abonelik: önceki döneme göre değişmedi."
    none = {**merchant_result("Netflix", "LastMonth", "2026-09-01", "2026-09-30", "0", 0), "currencies": []}
    chat, _, _, _ = service([SUBSCRIPTION_LAST_MONTH], gateway=gateway(none))

    reply = chat.answer("Geçen ay Netflix'e ne kadar harcadım?", TOKEN)

    assert "Bu dönemde bu işyerinde harcama bulunamadı." in [fact.text for fact in reply.facts]


def test_a_category_in_its_own_word_stays_a_category():
    chat, _, fake, _ = service([SUBSCRIPTION_LAST_MONTH], gateway=gateway(category_result(category_row())))

    chat.answer("Geçen ay abonelik harcamam ne kadar?", TOKEN)

    assert fake.calls[0][:2] == ("get_category_spending", {"categoryCode": "SUBSCRIPTION", "period": "LastMonth"})


def test_two_brands_are_no_one_merchant_and_stay_as_the_model_chose():
    chat, _, fake, _ = service([SUBSCRIPTION_LAST_MONTH], gateway=gateway(category_result(category_row())))

    chat.answer("Geçen ay Netflix ve Spotify'a ne kadar harcadım?", TOKEN)

    assert fake.calls[0][0] == "get_category_spending"


def test_a_brand_with_days_keeps_its_days():
    days = {"kind": "Custom", "startDate": "2026-09-01", "endDate": "2026-09-05"}
    chat, _, fake, _ = service([ToolCall("get_category_spending", {"categoryCode": "SUBSCRIPTION", "period": "ThisMonth"})],
                               gateway=gateway({**NETFLIX_SEPTEMBER, "period": days}))

    chat.answer("1-5 Eylül arası Netflix'e ne kadar harcadım?", TOKEN, DAY)

    assert fake.calls[0][:2] == ("get_merchant_spending",
                                 {"merchant": "Netflix", "period": "Custom", "from": "2026-09-01", "to": "2026-09-05"})


def test_the_same_category_twice_for_a_brand_is_the_merchant_in_the_said_period():
    # S25 in the model run: "ödedim" made the model call the category detail twice, for this month and last month.
    twice = [ToolCall("get_category_spending", {"categoryCode": "SUBSCRIPTION", "period": "ThisMonth"}), SUBSCRIPTION_LAST_MONTH]
    chat, _, fake, _ = service(twice, gateway=gateway())

    reply = chat.answer("Geçen ay Netflix'e ne kadar ödedim?", TOKEN)

    assert reply.type == "answer"
    assert fake.calls[0][:2] == ("get_merchant_spending", {"merchant": "Netflix", "period": "LastMonth"})


def test_two_different_tools_for_a_brand_are_still_asked_back():
    # Only the category detail repeated is the brand's call; two tools stay a question back (CHAT-04 K2-A).
    two = [ToolCall("get_spending_summary", {"period": "LastMonth"}), SUBSCRIPTION_LAST_MONTH]
    chat, _, fake, _ = service(two, gateway=gateway())

    reply = chat.answer("Geçen ay Netflix'e ne kadar ödedim?", TOKEN)

    assert reply.type == "clarification" and fake.calls == []
