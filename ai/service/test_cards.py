"""The answer cards (UX-01 PR4): Murat's figures at the demo moment, checked by SQL (docs/local-ai.md "UX-01 PR4")."""

from decimal import Decimal

from cards import Bar, Block, Card, Change, Headline, Point, Stat, card_texts, cards_for
from contracts import Fact

D = Decimal


def period(kind: str, start: str, end: str) -> dict:
    return {"kind": kind, "startDate": start, "endDate": end, "startUtc": "x", "endUtc": "y"}


SEPTEMBER = period("LastMonth", "2026-09-01", "2026-09-30")
AUGUST = period("Custom", "2026-08-01", "2026-08-31")


# --- get_spending_summary


def summary_row(currency="TRY", total="53388.39", count=20, unclassified="0"):
    return {"currencyCode": currency, "totalAmount": D(total), "transactionCount": count,
            "uncategorizedAmount": D(unclassified), "uncategorizedCount": 0}


def test_the_summary_card_is_the_total_and_the_count():
    result = {"period": SEPTEMBER, "currencies": [summary_row()]}

    assert cards_for("get_spending_summary", {"period": "LastMonth"}, result) == [Card(
        title="Harcama · Eylül 2026",
        headline=Headline(D("53388.39"), "TRY", "53.388,39 TL"),
        change=None,
        simple=(Block("stats", items=(Stat("İşlem", "20"),)),),
        detailed=(),
        sentence="Eylül 2026: 20 harcama, toplam 53.388,39 TL.")]


def test_two_currencies_are_two_cards_never_one_total():
    result = {"period": SEPTEMBER, "currencies": [summary_row(total="10490.00", count=9), summary_row("USD", "37.99", 2)]}

    cards = cards_for("get_spending_summary", {"period": "LastMonth"}, result)

    assert [(c.title, c.headline.text) for c in cards] == [
        ("Harcama · Eylül 2026 (TRY)", "10.490,00 TL"), ("Harcama · Eylül 2026 (USD)", "37,99 USD")]


def test_the_unclassified_part_is_a_stat_when_there_is_one():
    result = {"period": SEPTEMBER, "currencies": [summary_row(total="735.00", count=5, unclassified="145.00")]}

    card = cards_for("get_spending_summary", {"period": "LastMonth"}, result)[0]

    assert card.simple[0].items == (Stat("İşlem", "5"), Stat("Sınıflandırılmamış", "145,00 TL"))


def test_no_spending_is_no_card():
    assert cards_for("get_spending_summary", {"period": "LastMonth"}, {"period": SEPTEMBER, "currencies": []}) == []


# --- get_category_breakdown


def category(name, amount, share):
    return {"categoryCode": name.upper(), "displayName": name, "amount": D(amount), "transactionCount": 1, "percentage": D(share)}


BREAKDOWN = {"period": SEPTEMBER, "currencies": [{"currencyCode": "TRY", "totalAmount": D("53388.39"), "categories": [
    category("Faturalar", "27439.90", "51.40"), category("Market", "18016.50", "33.75"), category("Yakıt", "3867.00", "7.24"),
    category("Yeme & İçme", "1942.50", "3.64"), category("Diğer", "1250.00", "2.34"), category("Eğlence", "642.50", "1.20"),
    category("Abonelik", "229.99", "0.43")]}]}


def test_the_category_card_shows_three_categories_simply_and_the_rest_in_detail():
    card = cards_for("get_category_breakdown", {"period": "LastMonth"}, BREAKDOWN)[0]

    assert (card.title, card.headline.text, card.change) == ("Kategoriler · Eylül 2026", "53.388,39 TL", None)
    # The bar is the share of the largest row, for drawing only; the text is the backend's percentage.
    assert card.simple == (Block("bars", rows=(
        Bar("Faturalar", D("27439.90"), "27.439,90 TL · %51,40", D("1.00")),
        Bar("Market", D("18016.50"), "18.016,50 TL · %33,75", D("0.66")),
        Bar("Yakıt", D("3867.00"), "3.867,00 TL · %7,24", D("0.14")))),)
    assert [r.label for r in card.detailed[0].rows] == ["Yeme & İçme", "Diğer", "Eğlence", "Abonelik"]
    assert card.detailed[0].rows[-1] == Bar("Abonelik", D("229.99"), "229,99 TL · %0,43", D("0.01"))
    # Simulator trial (19 Nov 2026): no sentence; it only repeated the first bar.
    assert card.sentence == ""


def test_three_categories_or_fewer_have_no_detailed_part():
    few = {"period": SEPTEMBER, "currencies": [{**BREAKDOWN["currencies"][0], "categories": BREAKDOWN["currencies"][0]["categories"][:2]}]}

    assert cards_for("get_category_breakdown", {"period": "LastMonth"}, few)[0].detailed == ()


# --- get_category_spending (PR3)


def merchant(name, amount, share):
    return {"merchant": name, "displayName": name, "amount": D(amount), "transactionCount": 1, "percentage": D(share)}


def month(number, amount, partial=False):
    return {"year": 2026, "month": number, "isPartial": partial, "amount": D(amount), "transactionCount": 1}


def category_row(**changes):
    row = {
        "currencyCode": "TRY", "amount": D("18016.50"), "transactionCount": 8, "averagePerTransaction": D("2252.06"),
        "previousAmount": D("11944.50"), "previousCount": 5, "change": D("6072.00"), "percentageChange": D("50.84"),
        "totalSpending": D("53388.39"), "shareOfSpending": D("33.75"),
        "merchants": [merchant("BİM", "6216.00", "34.50"), merchant("Migros Jet", "5317.50", "29.51"),
                      merchant("Migros Sanal Market", "3577.50", "19.86"), merchant("A101", "2905.50", "16.13")],
        "largest": {"transactionId": "t", "amount": D("3476.00"), "merchant": "BİM", "localDate": "2026-09-03"},
        "months": [month(4, "8654.00"), month(5, "10339.00"), month(6, "12896.00"), month(7, "16078.50"), month(8, "11944.50"),
                   month(9, "18016.50"), month(10, "5133.00", partial=True)],
        "monthlyAverage": D("12988.08"), "percentageVsAverage": D("38.72")}
    return {**row, **changes}


def category_result(*rows, current=SEPTEMBER, previous=AUGUST):
    return {"period": current, "previousPeriod": previous, "categoryCode": "GROCERY", "displayName": "Market",
            "months": [], "currencies": list(rows)}


GROCERY_LAST_MONTH = {"categoryCode": "GROCERY", "period": "LastMonth"}


def test_the_category_detail_card_is_the_one_of_the_design():
    card = cards_for("get_category_spending", GROCERY_LAST_MONTH, category_result(category_row()))[0]

    assert card.title == "Market · Eylül 2026"
    assert card.headline == Headline(D("18016.50"), "TRY", "18.016,50 TL")
    assert card.change == Change(D("50.84"), "up", "Ağustos 2026", "11.944,50 TL", "%50,84 artış")
    assert card.simple == (Block("bars", rows=(
        Bar("BİM", D("6216.00"), "6.216,00 TL · %34,50", D("1.00")),
        Bar("Migros Jet", D("5317.50"), "5.317,50 TL · %29,51", D("0.86")),
        Bar("Migros Sanal Market", D("3577.50"), "3.577,50 TL · %19,86", D("0.58")),
        Bar("A101", D("2905.50"), "2.905,50 TL · %16,13", D("0.47")))),)
    series, stats = card.detailed
    assert series.kind == "series" and series.highlight == "Eyl"
    assert series.points[0] == Point("Nis", D("8654.00"), "8.654,00 TL", D("0.48"), False)
    assert series.points[-1] == Point("Eki", D("5133.00"), "5.133,00 TL", D("0.28"), True)
    assert stats == Block("stats", items=(
        Stat("6 ay ortalaması", "12.988,08 TL"), Stat("Ortalamaya göre", "%38,72 üstünde"), Stat("İşlem", "8"),
        Stat("İşlem başı", "2.252,06 TL"), Stat("En büyük işlem", "3.476,00 TL · BİM · 3 Eylül 2026"),
        Stat("Harcamadaki payı", "%33,75")))
    assert card.sentence == "Market: önceki döneme göre 6.072,00 TL fazla."


def test_this_months_category_detail_names_the_same_days_and_has_no_average_comparison():
    row = category_row(amount=D("5133.00"), transactionCount=2, averagePerTransaction=D("2566.50"), previousAmount=D("7176.50"),
                       previousCount=3, change=D("-2043.50"), percentageChange=D("-28.47"), totalSpending=D("8241.40"),
                       shareOfSpending=D("62.28"), merchants=[merchant("Migros Jet", "5133.00", "100.00")],
                       percentageVsAverage=None)
    result = category_result(row, current=period("ThisMonth", "2026-10-01", "2026-10-10"),
                             previous=period("Custom", "2026-09-01", "2026-09-10"))

    card = cards_for("get_category_spending", {"categoryCode": "GROCERY", "period": "ThisMonth"}, result)[0]

    assert card.title == "Market · 1-10 Ekim 2026"
    assert card.change == Change(D("-28.47"), "down", "1-10 Eylül 2026", "7.176,50 TL", "%28,47 azalış")
    # The running month is no complete window: nothing is highlighted and no average comparison is made up.
    assert card.detailed[0].highlight is None
    assert "Ortalamaya göre" not in [s.label for s in card.detailed[1].items]
    assert card.sentence == "Market: önceki döneme göre 2.043,50 TL az."


def test_a_period_without_purchases_has_no_bars_and_says_so():
    row = category_row(amount=D("0"), transactionCount=0, averagePerTransaction=None, previousAmount=D("0"), previousCount=0,
                       change=D("0"), percentageChange=None, shareOfSpending=D("0.00"), merchants=[], largest=None,
                       percentageVsAverage=None)

    card = cards_for("get_category_spending", GROCERY_LAST_MONTH, category_result(row))[0]

    assert card.simple == ()
    assert card.change == Change(None, "same", "Ağustos 2026", "0,00 TL", "önceki dönemde harcama yok")
    assert {"İşlem başı", "En büyük işlem"}.isdisjoint(s.label for s in card.detailed[1].items)
    assert card.sentence == "Market: bu dönemde harcama yok."


def test_only_the_first_five_merchants_are_bars_as_in_the_facts():
    row = category_row(merchants=[merchant(f"İşyeri {i}", "10.00", "10.00") for i in range(7)])

    card = cards_for("get_category_spending", GROCERY_LAST_MONTH, category_result(row))[0]

    assert len(card.simple[0].rows) == 5


# --- get_merchant_spending


def test_the_merchant_card_is_the_total_and_the_count():
    result = {"period": SEPTEMBER, "match": {"status": "Matched", "merchant": "BİM", "isPartial": False, "candidates": []},
              "currencies": [{"currencyCode": "TRY", "totalAmount": D("6216.00"), "transactionCount": 2}]}

    assert cards_for("get_merchant_spending", {"merchant": "BİM", "period": "LastMonth"}, result) == [Card(
        title="BİM · Eylül 2026", headline=Headline(D("6216.00"), "TRY", "6.216,00 TL"), change=None,
        simple=(Block("stats", items=(Stat("İşlem", "2"),)),), detailed=(), sentence="BİM: 2 harcama, toplam 6.216,00 TL.")]


def test_an_unmatched_merchant_has_no_card():
    result = {"period": SEPTEMBER, "match": {"status": "Ambiguous", "merchant": None, "candidates": ["A", "B"]}, "currencies": []}

    assert cards_for("get_merchant_spending", {"merchant": "x", "period": "LastMonth"}, result) == []


# --- compare_spending


def change(name, current, previous, delta):
    return {"categoryCode": name.upper(), "displayName": name, "currentAmount": D(current), "previousAmount": D(previous),
            "change": D(delta)}


COMPARISON = {"current": SEPTEMBER, "previous": AUGUST, "currencies": [{
    "currencyCode": "TRY", "currentAmount": D("53388.39"), "currentCount": 20, "previousAmount": D("22569.89"), "previousCount": 15,
    "change": D("30818.50"), "percentageChange": D("136.55"), "categories": [
        change("Faturalar", "27439.90", "2759.90", "24680.00"), change("Market", "18016.50", "11944.50", "6072.00"),
        change("Diğer", "1250.00", "0", "1250.00"), change("Yakıt", "3867.00", "4691.00", "-824.00"),
        change("Yeme & İçme", "1942.50", "2365.50", "-423.00"), change("Eğlence", "642.50", "579.00", "63.50"),
        change("Abonelik", "229.99", "229.99", "0.00")]}]}


def test_the_comparison_card_shows_what_drove_the_change():
    card = cards_for("compare_spending", {"comparison": "PreviousFullMonth"}, COMPARISON)[0]

    assert (card.title, card.headline.text) == ("Karşılaştırma · Eylül 2026", "53.388,39 TL")
    assert card.change == Change(D("136.55"), "up", "Ağustos 2026", "22.569,89 TL", "%136,55 artış")
    assert card.simple == (Block("bars", rows=(
        Bar("Faturalar", D("24680.00"), "24.680,00 TL artış", D("1.00"), "up"),
        Bar("Market", D("6072.00"), "6.072,00 TL artış", D("0.25"), "up"),
        Bar("Diğer", D("1250.00"), "1.250,00 TL artış", D("0.05"), "up"))),)
    # Five changed categories, as the facts list; an unchanged one is not a driver.
    assert card.detailed == (Block("bars", rows=(
        Bar("Yakıt", D("824.00"), "824,00 TL azalış", D("0.03"), "down"),
        Bar("Yeme & İçme", D("423.00"), "423,00 TL azalış", D("0.02"), "down"))),)
    assert card.sentence == "En büyük değişim Faturalar: 24.680,00 TL artış."


# --- get_monthly_trend


def window(number, start, end, partial=False):
    return {"year": 2026, "month": number, "isPartial": partial, "period": period("Custom", start, end)}


TREND = {"months": [window(4, "2026-04-01", "2026-04-30"), window(9, "2026-09-01", "2026-09-30"),
                    window(10, "2026-10-01", "2026-10-10", partial=True)],
         "merchantMatch": None, "currencies": [{"currencyCode": "TRY", "months": [
             month(4, "19852.89"), month(9, "53388.39"), month(10, "8241.40", partial=True)]}]}


def test_the_trend_card_is_the_months_with_the_highest_and_lowest_complete_month():
    card = cards_for("get_monthly_trend", {"months": 2}, TREND)[0]

    # The title says the days like the facts do ("Dönem"), so the number check finds every figure of it.
    assert (card.title, card.headline.text, card.change) == ("Aylık harcama · 1 Nisan - 10 Ekim 2026", "53.388,39 TL", None)
    assert card.simple == (Block("series", points=(
        Point("Nis", D("19852.89"), "19.852,89 TL", D("0.37"), False),
        Point("Eyl", D("53388.39"), "53.388,39 TL", D("1.00"), False),
        Point("Eki", D("8241.40"), "8.241,40 TL", D("0.15"), True)), highlight="Eyl"),)
    # K5-D: chosen, not computed; the running month is not a whole month.
    assert card.detailed == (Block("stats", items=(
        Stat("En yüksek ay", "Eylül 2026 · 53.388,39 TL"), Stat("En düşük ay", "Nisan 2026 · 19.852,89 TL"))),)
    assert card.sentence == "Son tam ay Eylül 2026: 53.388,39 TL."


# --- what every card shares


def test_a_tool_without_cards_and_an_unreadable_result_give_none():
    assert cards_for("get_recurring_payments", {}, {"payments": []}) == []
    assert cards_for("get_spending_summary", {"period": "LastMonth"}, {"unexpected": True}) == []


def test_every_text_of_a_card_is_listed_for_the_number_check():
    card = cards_for("get_category_spending", GROCERY_LAST_MONTH, category_result(category_row()))[0]

    texts = card_texts(card)

    assert "Market · Eylül 2026" in texts and "11.944,50 TL" in texts and "%38,72 üstünde" in texts
    assert "Market: önceki döneme göre 6.072,00 TL fazla." in texts and "8.654,00 TL" in texts


def test_a_zero_largest_row_draws_no_bar_at_all():
    zero = {"period": SEPTEMBER, "currencies": [{"currencyCode": "TRY", "totalAmount": D("0"), "categories": [
        category("Market", "0", "0")]}]}

    assert cards_for("get_category_breakdown", {"period": "LastMonth"}, zero)[0].simple[0].rows[0].share == D("0")


def test_cards_are_checked_against_the_facts_elsewhere_so_the_fact_type_is_the_same():
    # The chat compares card_texts with the facts of the same result (K6-A); both speak the formatting of formatting.py.
    assert Fact(label="Toplam harcama", text="53.388,39 TL").text == cards_for(
        "get_spending_summary", {"period": "LastMonth"}, {"period": SEPTEMBER, "currencies": [summary_row()]})[0].headline.text


def test_an_unchanged_category_is_no_driver_even_when_there_is_room():
    # The backend puts the biggest change first, so an unchanged category is last; with few categories it would still fit.
    few = {**COMPARISON, "currencies": [{**COMPARISON["currencies"][0], "categories": [
        change("Faturalar", "27439.90", "2759.90", "24680.00"), change("Abonelik", "229.99", "229.99", "0.00")]}]}

    card = cards_for("compare_spending", {"comparison": "PreviousFullMonth"}, few)[0]

    assert [bar.label for bar in card.simple[0].rows] == ["Faturalar"] and card.detailed == ()
