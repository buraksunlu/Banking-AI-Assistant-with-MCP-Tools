"""ACC-01b K5-B: the cards of the four everyday banking tools. The rows come from the tool's own result (the same call whose facts
check them), so a card with two directions or two currencies works like one; the whole list is the answer's link (PR2)."""

from decimal import Decimal

import pytest

from answer_check import check_answer
from cards import Block, Entry, Headline, card_texts, cards_for
from facts import outcome_for
from test_facts_accounts import (CAN_ACCOUNTS, CAN_RECEIVED, HASAN_TO_SELIN, MURAT_ACCOUNTS, MURAT_OVER_2000, MURAT_SALARY,
                                 MURAT_SEARCH, SEPTEMBER, ZEYNEP_ACCOUNTS, item, search)

D = Decimal


def test_the_balance_card_is_the_total_and_each_account():
    card, = cards_for("get_accounts", {}, MURAT_ACCOUNTS)

    assert (card.title, card.headline, card.change) == ("Kişisel hesaplar", Headline(D("644659.42"), "TRY", "644.659,42 TL"), None)
    assert card.simple == (Block("list", entries=(Entry("Murat aile TRY", "•••• 001", "644.659,42 TL"),)),)
    # Simulator trial 17 Nov 2026 (K1-A): no sentence; it repeated the figure above it.
    assert (card.detailed, card.sentence) == ((), "")


def test_business_accounts_are_a_card_of_their_own():
    personal, business = cards_for("get_accounts", {}, CAN_ACCOUNTS)

    assert (personal.title, personal.headline.text) == ("Kişisel hesaplar", "72.743,21 TL")
    assert (business.title, business.headline.text) == ("İşletme hesapları", "19.720,00 TL")
    assert business.simple == (Block("list", entries=(Entry("Can Kırtasiye – Moda", "•••• 001", "19.720,00 TL"),)),)
    assert business.sentence == ""


def test_two_currencies_are_two_cards():
    lira, dollar = cards_for("get_accounts", {}, ZEYNEP_ACCOUNTS)

    assert [(c.title, c.headline.text) for c in (lira, dollar)] == [("Kişisel hesaplar (TRY)", "135.422,50 TL"),
                                                                   ("Kişisel hesaplar (USD)", "11.771,13 USD")]
    assert dollar.simple == (Block("list", entries=(Entry("Zeynep USD", "•••• 001", "11.771,13 USD"),)),)


def test_the_income_card():
    card, = cards_for("get_income", {"period": "LastMonth"}, MURAT_SALARY)

    assert (card.title, card.headline.text) == ("Gelir · Eylül 2026", "85.000,00 TL")
    assert card.simple == (Block("list", entries=(Entry("Maaş", "Murat aile TRY · 15 Eylül 2026", "85.000,00 TL"),)),)
    # Simulator trial (19 Nov 2026): no sentence; it repeated the headline. Transfers and searches keep theirs (count, direction).
    assert card.sentence == ""


def test_the_transfer_card_names_the_person_and_its_direction():
    card, = cards_for("get_transfers", {"period": "Last3Months", "direction": "Sent", "counterparty": "Selin"}, HASAN_TO_SELIN)

    assert (card.title, card.headline.text) == ("Gönderilen · Selin K*** · 11 Temmuz - 10 Ekim 2026", "9.000,00 TL")
    assert card.simple == (Block("list", entries=tuple(Entry("Selin K***", d, "3.000,00 TL") for d in
                                                       ("26 Eylül 2026", "26 Ağustos 2026", "26 Temmuz 2026"))),)
    assert card.sentence == "11 Temmuz - 10 Ekim 2026: 3 transfer gönderdiniz, toplam 9.000,00 TL."


def test_money_sent_and_received_are_two_cards_never_netted():
    both = search(SEPTEMBER, [item("3000.00", "2026-09-26", "Transfer", "Debit", counterparty="Selin K***"),
                              item("500.00", "2026-09-10", "Transfer", "Credit", counterparty="Ege Y***")],
                  [("TRY", "Debit", 1, "3000.00"), ("TRY", "Credit", 1, "500.00")])

    sent, received = cards_for("get_transfers", {"period": "LastMonth"}, both)

    assert [(c.title, c.headline.text) for c in (sent, received)] == [("Gönderilen · Eylül 2026", "3.000,00 TL"),
                                                                     ("Gelen · Eylül 2026", "500,00 TL")]
    assert received.simple == (Block("list", entries=(Entry("Ege Y***", "10 Eylül 2026", "500,00 TL"),)),)
    assert received.sentence == "Eylül 2026: 1 transfer geldi, toplam 500,00 TL."


def test_an_own_account_is_named_as_such():
    card, = cards_for("get_transfers", {"period": "ThisMonth"}, CAN_RECEIVED)
    assert card.simple[0].entries == (Entry("Can Kırtasiye – Moda", "kendi hesabınız · 1 Ekim 2026", "18.000,00 TL"),)


def test_the_search_card_says_its_limit_and_shows_five_rows():
    card, = cards_for("search_transactions", MURAT_SEARCH, MURAT_OVER_2000)

    assert (card.title, card.headline.text) == ("2.000,00 TL ve üstü harcamalar · Eylül 2026", "41.363,50 TL")
    assert [e.label for e in card.simple[0].entries] == ["Okul", "Migros Jet", "Shell"]
    assert card.simple[0].entries[0] == Entry("Okul", "Diğer · 5 Eylül 2026", "25.000,00 TL")
    # The facts list five rows; the card shows no row whose figure they lack. The rest is the answer's link.
    assert [e.label for e in card.detailed[0].entries] == ["A101", "Sınıflandırılmamış"]
    assert card.detailed[0].entries[1] == Entry("Sınıflandırılmamış", "27 Eylül 2026", "2.100,00 TL")
    assert card.sentence == "Eylül 2026: 7 harcama, toplam 41.363,50 TL."


def test_nothing_found_or_an_unknown_person_is_no_card():
    empty = search(SEPTEMBER, [], [])
    unknown = search(SEPTEMBER, [], [], counterparty={"status": "NotFound", "name": None, "candidates": []})
    assert cards_for("get_income", {"period": "LastMonth"}, empty) == []
    assert cards_for("get_transfers", {"period": "LastMonth", "counterparty": "Annem"}, unknown) == []
    assert cards_for("get_accounts", {}, {"accounts": [], "totals": []}) == []


@pytest.mark.parametrize("tool, arguments, result", [
    ("get_accounts", {}, MURAT_ACCOUNTS), ("get_accounts", {}, CAN_ACCOUNTS), ("get_accounts", {}, ZEYNEP_ACCOUNTS),
    ("get_income", {"period": "LastMonth"}, MURAT_SALARY),
    ("get_transfers", {"period": "Last3Months", "direction": "Sent", "counterparty": "Selin"}, HASAN_TO_SELIN),
    ("get_transfers", {"period": "ThisMonth"}, CAN_RECEIVED), ("search_transactions", MURAT_SEARCH, MURAT_OVER_2000),
])
def test_every_text_of_the_account_cards_is_in_their_facts(tool, arguments, result):
    facts = outcome_for(tool, arguments, result).facts
    for card in cards_for(tool, arguments, result):
        assert check_answer("", facts, "\n".join(card_texts(card))).invented_numbers == (), card.title
