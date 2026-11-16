"""The cards of a chat turn (UX-01 PR4): built by code next to the facts and checked against them (K6-A); the model still
explains every answer until the phone draws the cards (K1-B)."""

from decimal import Decimal

import chat as chat_module
from cards import Block, Card, Headline, Stat
from test_chat import ANSWER, SUMMARY_YESTERDAY, TOKEN, YESTERDAY_FACTS, FakeGateway, service
from chat import ToolCall


def test_an_answer_carries_its_card_and_the_models_explanation():
    chat, _, _, explainer = service([SUMMARY_YESTERDAY])

    reply = chat.answer("Dün ne harcadım?", TOKEN)

    assert reply.answer == ANSWER and explainer.calls[0][1] == YESTERDAY_FACTS
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
