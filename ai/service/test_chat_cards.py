"""The cards of a chat turn (UX-01 PR4): built by code next to the facts and checked against them (K6-A). Since the phone draws
them (PR5 K2-A), an answer with a card is the card's own sentence and the model explains only answers without one."""

from decimal import Decimal

import chat as chat_module
from cards import Block, Card, Headline, Stat
from test_cards import category_result, category_row, summary_row
from test_chat import ANSWER, SUMMARY_YESTERDAY, TOKEN, YESTERDAY, FakeGateway, service
from chat import ToolCall


def test_an_answer_with_a_card_is_the_cards_sentence_and_the_model_does_not_explain():
    chat, _, _, explainer = service([SUMMARY_YESTERDAY])

    reply = chat.answer("Dün ne harcadım?", TOKEN)

    assert reply.answer == "9 Ekim 2026: 1 harcama, toplam 145,00 TL." and explainer.calls == []
    assert reply.cards == [Card(
        "Harcama · 9 Ekim 2026", Headline(Decimal("145.00"), "TRY", "145,00 TL"), None,
        (Block("stats", items=(Stat("İşlem", "1"), Stat("Sınıflandırılmamış", "145,00 TL"))),), (),
        "9 Ekim 2026: 1 harcama, toplam 145,00 TL.")]


def test_a_card_with_a_figure_the_facts_lack_is_dropped(monkeypatch):
    invented = Card("Harcama · 9 Ekim 2026", Headline(Decimal("999.00"), "TRY", "999,00 TL"), None, (), (), "Uydurma.")
    monkeypatch.setattr(chat_module, "cards_for", lambda tool, arguments, result: [invented])
    chat, _, _, _ = service([SUMMARY_YESTERDAY])

    reply = chat.answer("Dün ne harcadım?", TOKEN)

    # The answer still stands: a card is never a reason to fail the turn.
    assert (reply.type, reply.answer, reply.cards) == ("answer", ANSWER, [])


def test_a_tool_without_cards_answers_as_before():
    upcoming = ToolCall("get_upcoming_payments", {"days": 7})
    gateway = FakeGateway({"from": "2026-10-10", "to": "2026-10-16", "payments": [], "totals": []})
    chat, _, _, _ = service([upcoming], gateway=gateway)

    reply = chat.answer("Önümüzdeki 7 günde hangi ödemelerim çıkacak?", TOKEN)

    assert reply.type == "answer" and reply.cards == []


def test_a_question_back_has_no_card():
    # The model guessed a period the user did not say (CHAT-03): the period is asked and nothing runs.
    chat, _, _, _ = service([ToolCall("get_spending_summary", {"period": "ThisMonth"})])

    reply = chat.answer("Ne kadar harcadım?", TOKEN)

    assert reply.type == "clarification" and reply.cards == []


def test_two_cards_answer_with_both_sentences_one_per_line():
    two = {**YESTERDAY, "currencies": [summary_row("TRY", "145.00", 1), summary_row("USD", "20.00", 1)]}
    chat, _, _, explainer = service([SUMMARY_YESTERDAY], gateway=FakeGateway(two))

    reply = chat.answer("Dün ne harcadım?", TOKEN)

    assert len(reply.cards) == 2 and explainer.calls == []
    assert reply.answer == "\n".join(card.sentence for card in reply.cards)


def test_a_panel_category_question_runs_its_tool_without_the_model():
    # UX-01 PR5 K1-C: the home panel's row sends the follow-up pattern of PR3; it was never offered, yet code knows it.
    chat, selector, gateway, explainer = service([], gateway=FakeGateway(category_result(category_row())))

    reply = chat.answer("Bu ay market harcamam ne kadar?", TOKEN)

    assert selector.calls == [] and explainer.calls == []
    assert gateway.calls[0][:2] == ("get_category_spending", {"categoryCode": "GROCERY", "period": "ThisMonth"})
    assert reply.type == "answer" and len(reply.cards) == 1


def test_a_category_without_a_panel_word_goes_the_usual_way():
    chat, selector, _, _ = service([SUMMARY_YESTERDAY])

    chat.answer("Bu ay diğer harcamam ne kadar?", TOKEN)

    assert len(selector.calls) == 1


def test_a_budget_keeps_the_answer_code_wrote_and_carries_its_card():
    # UX-01 PR6 K5-B: the budget's text was written by code (CHAT-10 PR3b), not the model; it stays the answer, the card goes with it.
    from test_chat import BUDGET_RESULT

    call = ToolCall("check_budget", {"amount": 3000, "currencyCode": "TRY"})
    chat, _, _, explainer = service([call], gateway=FakeGateway(BUDGET_RESULT))

    reply = chat.answer("Bu ay 3.000 TL bütçem var, ne durumdayım?", TOKEN)

    assert reply.answer.startswith("Bu ay (1-10 Ekim 2026) 735,00 TL harcadınız (5 işlem);") and explainer.calls == []
    assert [card.title for card in reply.cards] == ["Bütçe · 1-10 Ekim 2026"]
    assert reply.cards[0].sentence == "Bu hızla bütçenin içinde kalır."
