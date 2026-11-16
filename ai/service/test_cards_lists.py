"""The list cards (UX-01 PR6a): merchants, the largest purchases, the regular and the upcoming payments. Murat's and Zeynep's
results at the demo moment (MCP server, 16 Nov 2026); the totals were checked by SQL before (53.388,39, 609,99)."""

from decimal import Decimal

from cards import Bar, Block, Card, Entry, Headline, card_texts, cards_for
from facts import RECURRING_RULE, UPCOMING_RULE, outcome_for
from answer_check import check_answer
from test_cards import SEPTEMBER, period

D = Decimal


def merchant(name, amount, share, count=1):
    return {"merchant": name, "displayName": name, "amount": D(amount), "percentage": D(share), "transactionCount": count}


MURAT_MERCHANTS = {"period": SEPTEMBER, "currencies": [{"currencyCode": "TRY", "totalAmount": D("53388.39"), "merchants": [
    merchant("Okul Taksidi", "25000.00", "46.83"), merchant("BİM", "6216.00", "11.64", 2), merchant("Migros Jet", "5317.50", "9.96", 3),
    merchant("Akaryakıt İstasyonu", "3867.00", "7.24", 2), merchant("Migros Sanal Market", "3577.50", "6.70", 2),
    merchant("A101", "2905.50", "5.44")]}]}


def test_the_merchant_card_is_the_categories_card_for_merchants():
    card = cards_for("get_merchant_breakdown", {"period": "LastMonth"}, MURAT_MERCHANTS)[0]

    assert (card.title, card.headline.text) == ("İşyerleri · Eylül 2026", "53.388,39 TL")
    assert card.simple == (Block("bars", rows=(
        Bar("Okul Taksidi", D("25000.00"), "25.000,00 TL · %46,83", D("1.00")),
        Bar("BİM", D("6216.00"), "6.216,00 TL · %11,64", D("0.25")),
        Bar("Migros Jet", D("5317.50"), "5.317,50 TL · %9,96", D("0.21")))),)
    # K7-A: rows four and five in the detail; the sixth merchant is not in the facts (LIST_LIMIT), so not on the card.
    assert [bar.label for bar in card.detailed[0].rows] == ["Akaryakıt İstasyonu", "Migros Sanal Market"]
    assert card.sentence == "En büyük pay Okul Taksidi: %46,83."


def item(merchant_name, category, amount, local_date, currency="TRY"):
    return {"merchant": merchant_name, "categoryDisplayName": category, "amount": D(amount), "currencyCode": currency,
            "localDate": local_date}


MURAT_HIGHEST = {"period": SEPTEMBER, "currencies": [{"currencyCode": "TRY", "items": [
    item("Okul Taksidi", "Faturalar", "25000.00", "2026-09-15"), item("BİM", "Market", "3476.00", "2026-09-03"),
    item("Migros Jet", "Market", "3121.00", "2026-09-30"), item("A101", "Market", "2905.50", "2026-09-20"),
    item("BİM", "Market", "2740.00", "2026-09-26")]}]}


def test_the_largest_purchases_are_a_list_with_where_and_when():
    card = cards_for("get_highest_transactions", {"period": "LastMonth"}, MURAT_HIGHEST)[0]

    assert (card.title, card.headline.text) == ("En büyük harcamalar · Eylül 2026", "25.000,00 TL")
    assert card.simple == (Block("list", entries=(
        Entry("Okul Taksidi", "Faturalar · 15 Eylül 2026", "25.000,00 TL"),
        Entry("BİM", "Market · 3 Eylül 2026", "3.476,00 TL"),
        Entry("Migros Jet", "Market · 30 Eylül 2026", "3.121,00 TL"))),)
    assert [entry.text for entry in card.detailed[0].entries] == ["2.905,50 TL", "2.740,00 TL"]
    assert card.sentence == "En büyüğü Okul Taksidi: 25.000,00 TL."


def test_a_purchase_without_merchant_or_category_says_so_and_only_its_day():
    result = {"period": SEPTEMBER, "currencies": [{"currencyCode": "TRY", "items": [item(None, None, "145.00", "2026-09-09")]}]}

    card = cards_for("get_highest_transactions", {"period": "LastMonth"}, result)[0]

    assert card.simple[0].entries == (Entry("Sınıflandırılmamış", "9 Eylül 2026", "145,00 TL"),)


def payment(name, category, currency, amount, last, following):
    return {"merchant": name, "categoryDisplayName": category, "currencyCode": currency,
            "pattern": {"occurrenceCount": 6, "latestAmount": D(amount), "lastDate": last, "nextExpectedDate": following}}


SIX_MONTHS = period("Last6Months", "2026-04-10", "2026-10-10")
MURAT_RECURRING = {"period": SIX_MONTHS, "payments": [
    payment("Su Faturası", "Faturalar", "TRY", "380.00", "2026-09-22", "2026-10-22"),
    payment("Netflix", "Abonelik", "TRY", "229.99", "2026-09-12", "2026-10-12")],
    "monthlyTotals": [{"currencyCode": "TRY", "monthlyTotal": D("609.99")}]}


def test_the_regular_payments_are_their_monthly_total_and_the_next_dates():
    card = cards_for("get_recurring_payments", {}, MURAT_RECURRING)[0]

    assert card.title == "Düzenli ödemeler · 10 Nisan - 10 Ekim 2026"
    assert card.headline == Headline(D("609.99"), "TRY", "609,99 TL")
    assert card.simple == (Block("list", entries=(
        Entry("Su Faturası", "Faturalar · sonraki 22 Ekim 2026", "380,00 TL / ay"),
        Entry("Netflix", "Abonelik · sonraki 12 Ekim 2026", "229,99 TL / ay"))),)
    assert card.detailed == ()
    assert (card.sentence, card.notes) == ("Aylık toplam 609,99 TL.", (RECURRING_RULE,))


ZEYNEP_RECURRING = {"period": SIX_MONTHS, "payments": [
    payment("Ortak Ofis", "Faturalar", "TRY", "3500.00", "2026-10-02", "2026-11-02"),
    payment("Mobil Hat Faturası", "Faturalar", "TRY", "450.00", "2026-10-10", "2026-11-10"),
    payment("Adobe", "Abonelik", "USD", "22.99", "2026-10-09", "2026-11-09"),
    payment("Figma", "Abonelik", "USD", "15.00", "2026-09-20", "2026-10-20")],
    "monthlyTotals": [{"currencyCode": "TRY", "monthlyTotal": D("3950.00")}, {"currencyCode": "USD", "monthlyTotal": D("37.99")}]}


def test_two_currencies_of_regular_payments_are_two_cards():
    cards = cards_for("get_recurring_payments", {}, ZEYNEP_RECURRING)

    assert [(c.title, c.headline.text) for c in cards] == [
        ("Düzenli ödemeler · 10 Nisan - 10 Ekim 2026 (TRY)", "3.950,00 TL"),
        ("Düzenli ödemeler · 10 Nisan - 10 Ekim 2026 (USD)", "37,99 USD")]
    assert [[e.label for e in c.simple[0].entries] for c in cards] == [["Ortak Ofis", "Mobil Hat Faturası"], ["Adobe", "Figma"]]


MURAT_UPCOMING = {"from": "2026-10-10", "to": "2026-10-17", "payments": [
    {"merchant": "Netflix", "categoryDisplayName": "Abonelik", "currencyCode": "TRY", "estimatedDate": "2026-10-12",
     "estimatedAmount": D("229.99"), "daysUntil": 2, "isOverdue": False}],
    "totals": [{"currencyCode": "TRY", "estimatedTotal": D("229.99")}]}


def test_the_upcoming_payments_are_the_estimated_total_and_how_soon():
    card = cards_for("get_upcoming_payments", {"days": 7}, MURAT_UPCOMING)[0]

    assert (card.title, card.headline.text) == ("Yaklaşan ödemeler · 10-17 Ekim 2026", "229,99 TL")
    assert card.simple == (Block("list", entries=(Entry("Netflix", "12 Ekim 2026 · 2 gün sonra", "229,99 TL"),)),)
    assert (card.sentence, card.notes) == ("Tahmini toplam 229,99 TL.", (UPCOMING_RULE,))


def test_an_overdue_payment_says_it_is_late():
    late = {**MURAT_UPCOMING, "payments": [{**MURAT_UPCOMING["payments"][0], "daysUntil": -3, "isOverdue": True}]}

    card = cards_for("get_upcoming_payments", {"days": 7}, late)[0]

    assert card.simple[0].entries[0].detail == "12 Ekim 2026 · 3 gün gecikti, henüz ödenmedi"


def test_nothing_to_list_is_no_card():
    # K8-A: the text answer says there is nothing; an empty card would only repeat it.
    assert cards_for("get_upcoming_payments", {"days": 7}, {"from": "2026-10-10", "to": "2026-10-17", "payments": [], "totals": []}) == []
    assert cards_for("get_recurring_payments", {}, {"period": SIX_MONTHS, "payments": [], "monthlyTotals": []}) == []
    assert cards_for("get_highest_transactions", {"period": "LastMonth"}, {"period": SEPTEMBER, "currencies": []}) == []
    assert cards_for("get_merchant_breakdown", {"period": "LastMonth"}, {"period": SEPTEMBER, "currencies": []}) == []


def test_every_text_of_the_list_cards_is_in_their_facts():
    # K6-A: the same check the chat runs; the entries and the notes are texts of the card too.
    for tool, arguments, result in [("get_merchant_breakdown", {"period": "LastMonth"}, MURAT_MERCHANTS),
                                    ("get_highest_transactions", {"period": "LastMonth"}, MURAT_HIGHEST),
                                    ("get_recurring_payments", {}, MURAT_RECURRING), ("get_recurring_payments", {}, ZEYNEP_RECURRING),
                                    ("get_upcoming_payments", {"days": 7}, MURAT_UPCOMING)]:
        facts = outcome_for(tool, arguments, result).facts
        for card in cards_for(tool, arguments, result):
            assert check_answer("", facts, "\n".join(card_texts(card))).invented_numbers == (), (tool, card.title)


def test_the_entries_and_the_notes_are_checked_too():
    card = cards_for("get_upcoming_payments", {"days": 7}, MURAT_UPCOMING)[0]
    invented = Card(card.title, card.headline, None, (Block("list", entries=(Entry("Netflix", "12 Ekim 2026", "999,00 TL"),)),), (),
                    card.sentence, ("Uydurma 777,00 TL.",))

    texts = card_texts(invented)

    assert "999,00 TL" in texts and "Uydurma 777,00 TL." in texts
