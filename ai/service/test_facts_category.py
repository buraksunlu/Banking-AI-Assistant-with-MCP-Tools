"""The category detail's facts (UX-01 PR3): Murat's groceries in September 2026, figures from docs/analytics.md."""

from decimal import Decimal

from contracts import Fact
from facts import ToolOutcome, outcome_for

ARGUMENTS = {"categoryCode": "GROCERY", "period": "LastMonth"}


def period(kind: str, start: str, end: str) -> dict:
    return {"kind": kind, "startDate": start, "endDate": end,
            "startUtc": "2026-08-31T21:00:00+00:00", "endUtc": "2026-09-30T21:00:00+00:00"}


def month(number: int, amount: str, count: int, partial: bool = False) -> dict:
    return {"year": 2026, "month": number, "isPartial": partial, "amount": Decimal(amount), "transactionCount": count}


SERIES = [month(4, "8654.00", 5), month(5, "10339.00", 7), month(6, "12896.00", 7), month(7, "16078.50", 8),
          month(8, "11944.50", 5), month(9, "18016.50", 8), month(10, "5133.00", 2, partial=True)]


def merchant(name: str | None, amount: str, count: int, share: str) -> dict:
    return {"merchant": name, "displayName": name or "Sınıflandırılmamış", "amount": Decimal(amount),
            "transactionCount": count, "percentage": Decimal(share)}


def row(**changes) -> dict:
    base = {
        "currencyCode": "TRY", "amount": Decimal("18016.50"), "transactionCount": 8,
        "averagePerTransaction": Decimal("2252.06"), "previousAmount": Decimal("11944.50"), "previousCount": 5,
        "change": Decimal("6072.00"), "percentageChange": Decimal("50.84"),
        "totalSpending": Decimal("53388.39"), "shareOfSpending": Decimal("33.75"),
        "merchants": [merchant("BİM", "6216.00", 2, "34.50"), merchant("Migros Jet", "5317.50", 3, "29.51"),
                      merchant("Migros Sanal Market", "3577.50", 2, "19.86"), merchant("A101", "2905.50", 1, "16.13")],
        "largest": {"transactionId": "30000000-0000-0000-0000-000000000001", "amount": Decimal("3476.00"), "merchant": "BİM",
                    "localDate": "2026-09-03"},
        "months": SERIES, "monthlyAverage": Decimal("12988.08"), "percentageVsAverage": Decimal("38.72"),
    }
    return {**base, **changes}


def result(*rows: dict) -> dict:
    return {"period": period("LastMonth", "2026-09-01", "2026-09-30"),
            "previousPeriod": period("Custom", "2026-08-01", "2026-08-31"),
            "categoryCode": "GROCERY", "displayName": "Market", "months": [], "currencies": list(rows)}


def facts(*pairs: tuple[str, str]) -> list[Fact]:
    return [Fact(label=label, text=text) for label, text in pairs]


def test_category_spending_states_every_figure_of_the_backend():
    assert outcome_for("get_category_spending", ARGUMENTS, result(row())) == ToolOutcome(facts(
        ("Dönem", "1-30 Eylül 2026"),
        ("Önceki dönem", "1-31 Ağustos 2026"),
        ("Kategori", "Market"),
        ("Market harcaması", "18.016,50 TL (8 işlem)"),
        ("Önceki dönem harcaması", "11.944,50 TL (5 işlem)"),
        ("Değişim", "6.072,00 TL artış (%50,84)"),
        ("Toplam harcamadaki payı", "%33,75 (toplam harcama 53.388,39 TL)"),
        ("İşlem başı ortalama", "2.252,06 TL"),
        ("En büyük işlem", "3.476,00 TL, BİM, 3 Eylül 2026"),
        ("BİM", "6.216,00 TL (%34,50)"),
        ("Migros Jet", "5.317,50 TL (%29,51)"),
        ("Migros Sanal Market", "3.577,50 TL (%19,86)"),
        ("A101", "2.905,50 TL (%16,13)"),
        ("Nisan 2026", "8.654,00 TL"),
        ("Mayıs 2026", "10.339,00 TL"),
        ("Haziran 2026", "12.896,00 TL"),
        ("Temmuz 2026", "16.078,50 TL"),
        ("Ağustos 2026", "11.944,50 TL"),
        ("Eylül 2026", "18.016,50 TL"),
        ("Ekim 2026 (devam eden ay)", "5.133,00 TL"),
        ("Son 6 tam ayın ortalaması", "12.988,08 TL"),
        ("Ortalamaya göre", "%38,72 üstünde"),
    ))


def test_a_period_without_spending_says_zero_and_leaves_out_what_does_not_exist():
    empty = row(amount=Decimal("0"), transactionCount=0, averagePerTransaction=None, previousAmount=Decimal("0"),
                previousCount=0, change=Decimal("0"), percentageChange=None, shareOfSpending=Decimal("0.00"),
                merchants=[], largest=None, percentageVsAverage=None)

    texts = dict((f.label, f.text) for f in outcome_for("get_category_spending", ARGUMENTS, result(empty)).facts)

    assert texts["Market harcaması"] == "0,00 TL (0 işlem)"
    # No rate from zero, no average of no purchases, no largest purchase.
    assert texts["Değişim"] == "değişmedi (önceki dönemde harcama olmadığı için yüzde hesaplanmaz)"
    assert not {"İşlem başı ortalama", "En büyük işlem", "Ortalamaya göre"} & texts.keys()


def test_below_the_average_and_an_unnamed_largest_purchase():
    below = row(percentageVsAverage=Decimal("-12.50"),
                largest={"transactionId": "x", "amount": Decimal("10.00"), "merchant": None, "localDate": "2026-09-03"})

    texts = dict((f.label, f.text) for f in outcome_for("get_category_spending", ARGUMENTS, result(below)).facts)

    assert texts["Ortalamaya göre"] == "%12,50 altında"
    assert texts["En büyük işlem"] == "10,00 TL, Sınıflandırılmamış, 3 Eylül 2026"


def test_two_currencies_are_two_sets_of_lines_never_added_up():
    usd = row(currencyCode="USD", amount=Decimal("9.99"), transactionCount=1, merchants=[], largest=None)

    outcome = outcome_for("get_category_spending", ARGUMENTS, result(row(), usd))

    labels = [f.label for f in outcome.facts]
    assert "Market harcaması (TRY)" in labels and "Market harcaması (USD)" in labels
    assert outcome.facts[-1] == Fact(label="Not", text="Farklı para birimleri toplanmaz, ayrı gösterilir.")


def test_a_category_never_used_says_so():
    assert outcome_for("get_category_spending", ARGUMENTS, result()).facts[-1] == Fact(
        label="Sonuç", text="Bu dönemde bu kategoride harcama bulunamadı.")


def test_more_than_five_merchants_name_only_how_many_are_left():
    many = row(merchants=[merchant(f"İşyeri {i}", "10.00", 1, "10.00") for i in range(7)])

    texts = dict((f.label, f.text) for f in outcome_for("get_category_spending", ARGUMENTS, result(many)).facts)

    assert "İşyeri 4" in texts and "İşyeri 5" not in texts
    assert texts["Not"] == "Bu dönemde 2 işyeri daha var; ilk 5 işyeri gösterildi."
