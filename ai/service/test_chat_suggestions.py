"""CHAT-10 PR2: the questions offered under the answers, through the real chat service.

Decisions: K1-C (nothing asked in the conversation is offered again; with no candidate left two general questions), K2-B (the
answer is looked at by counts only), K3-B (every offered text says its period), P2-A (a range is written out by code).
"""

from decimal import Decimal

from chat import ToolCall
from test_chat import THIS_MONTH_SUMMARY, TOKEN, FakeGateway, service

PERIOD = {"kind": "ThisMonth", "startDate": "2026-10-01", "endDate": "2026-10-10"}
CATEGORIES = {"period": PERIOD, "currencies": [{"currencyCode": "TRY", "totalAmount": Decimal("735.00"), "categories": [
    {"displayName": "Market", "amount": Decimal("590.00"), "percentage": Decimal("80.27")},
    {"displayName": "Sınıflandırılmamış", "amount": Decimal("145.00"), "percentage": Decimal("19.73")}]}]}
MERCHANTS = {"period": PERIOD, "currencies": [{"currencyCode": "TRY", "totalAmount": Decimal("735.00"), "merchants": [
    {"displayName": "Migros", "amount": Decimal("590.00"), "percentage": Decimal("80.27"), "transactionCount": 4}]}]}
HIGHEST = {"period": PERIOD, "currencies": [{"currencyCode": "TRY", "items": [
    {"amount": Decimal("300.00"), "currencyCode": "TRY", "merchant": "Migros", "categoryDisplayName": "Market",
     "localDate": "2026-10-03"}]}]}
NOTHING = {"period": PERIOD, "currencies": []}
ONE_PURCHASE = {"period": PERIOD, "currencies": [{"currencyCode": "TRY", "totalAmount": Decimal("85.00"), "transactionCount": 1,
                                                 "uncategorizedAmount": Decimal("0.00"), "uncategorizedCount": 0}]}

CAT_TM = "Bu ay kategorilere göre harcamam nasıl?"
MER_TM, MER_LM = "Bu ay harcamalarım işyerlerine göre nasıl dağıldı?", "Geçen ay harcamalarım işyerlerine göre nasıl dağıldı?"
HIGH_TM, HIGH_LM = "Bu ay en büyük harcamalarım neler?", "Geçen ay en büyük harcamalarım neler?"
SAME_DAYS = "Bu ay, geçen ayın aynı günlerine göre daha mı çok harcıyorum?"
TOTAL_LM = "Geçen ay toplam ne kadar harcadım?"
SUMMARY_TM = ToolCall("get_spending_summary", {"period": "ThisMonth"})


def test_tapping_down_the_breakdowns_never_offers_one_already_seen():
    chat, selector, gateway, _ = service([SUMMARY_TM], gateway=FakeGateway([THIS_MONTH_SUMMARY, CATEGORIES, MERCHANTS, HIGHEST]))

    first = chat.answer("Bu ay ne kadar harcadım?", TOKEN)
    second = chat.answer(CAT_TM, TOKEN)
    third = chat.answer(MER_TM, TOKEN)
    fourth = chat.answer(HIGH_TM, TOKEN)

    assert first.suggestions == [CAT_TM, MER_TM, SAME_DAYS]
    assert second.suggestions == [MER_TM, HIGH_TM, "Geçen ay kategorilere göre harcamam nasıl?"]
    assert third.suggestions == [HIGH_TM, MER_LM]
    assert fourth.suggestions == [HIGH_LM]
    # Each offered text ran its call without the model.
    assert len(selector.calls) == 1
    assert [call[0] for call in gateway.calls] == ["get_spending_summary", "get_category_breakdown", "get_merchant_breakdown",
                                                  "get_highest_transactions"]


def test_a_typed_question_counts_as_asked_too():
    turns = [[ToolCall("get_category_breakdown", {"period": "ThisMonth"})], [SUMMARY_TM]]
    chat, _, _, _ = service([], gateway=FakeGateway([CATEGORIES, THIS_MONTH_SUMMARY]), turns=turns)
    chat.answer("Bu ay kategorilere göre ne harcadım?", TOKEN)

    reply = chat.answer("Bu ay ne kadar harcadım?", TOKEN)

    assert CAT_TM not in reply.suggestions


def test_what_was_asked_belongs_to_its_token_only():
    chat, _, _, _ = service([SUMMARY_TM], gateway=FakeGateway([THIS_MONTH_SUMMARY, THIS_MONTH_SUMMARY]))
    chat.answer("Bu ay ne kadar harcadım?", TOKEN)

    reply = chat.answer("Bu ay ne kadar harcadım?", "another-users-token")

    assert reply.suggestions == [CAT_TM, MER_TM, SAME_DAYS]


def test_no_spending_offers_another_period_only():
    chat, _, _, _ = service([SUMMARY_TM], gateway=FakeGateway([NOTHING]))

    reply = chat.answer("Bu ay ne kadar harcadım?", TOKEN)

    assert reply.suggestions == [TOTAL_LM]


def test_one_purchase_is_not_broken_down():
    chat, _, _, _ = service([SUMMARY_TM], gateway=FakeGateway([ONE_PURCHASE]))

    reply = chat.answer("Bu ay ne kadar harcadım?", TOKEN)

    assert reply.suggestions == [SAME_DAYS, TOTAL_LM]


def test_a_failed_call_is_not_counted_as_asked():
    from answer_check import AnswerRejected
    from test_chat_retry import FailingOnce
    import pytest

    chat, _, _, _ = service([SUMMARY_TM], gateway=FakeGateway([THIS_MONTH_SUMMARY, CATEGORIES, THIS_MONTH_SUMMARY]),
                            explainer=FailingOnce(fail_at=2))
    chat.answer("Bu ay ne kadar harcadım?", TOKEN)
    with pytest.raises(AnswerRejected):
        chat.answer(CAT_TM, TOKEN)

    # The categories were never shown, so they are still offered.
    assert CAT_TM in chat.answer("Bu ay ne kadar harcadım?", TOKEN).suggestions
