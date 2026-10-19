from decimal import Decimal

from contracts import Fact
from facts import ToolOutcome, outcome_for


def period(kind: str, start: str, end: str) -> dict:
    return {"kind": kind, "startDate": start, "endDate": end,
            "startUtc": "2026-09-30T21:00:00+00:00", "endUtc": "2026-10-10T21:00:00+00:00"}


THIS_MONTH = period("ThisMonth", "2026-10-01", "2026-10-10")


def facts(*pairs: tuple[str, str]) -> list[Fact]:
    return [Fact(label=label, text=text) for label, text in pairs]


# Spending summary (docs/api.md: Ada this month 735, 5 purchases, 145 unclassified)


def test_summary_of_one_currency():
    result = {"period": THIS_MONTH, "currencies": [
        {"currencyCode": "TRY", "totalAmount": Decimal("735.00"), "transactionCount": 5,
         "uncategorizedAmount": Decimal("145.00"), "uncategorizedCount": 1}]}

    assert outcome_for("get_spending_summary", {"period": "ThisMonth"}, result) == ToolOutcome(facts(
        ("Dönem", "1-10 Ekim 2026"),
        ("Toplam harcama", "735,00 TL"),
        ("İşlem sayısı", "5"),
        ("Sınıflandırılmamış", "145,00 TL"),
    ))


def test_summary_without_unclassified_spending_has_no_such_line():
    result = {"period": period("LastWeek", "2026-09-28", "2026-10-04"), "currencies": [
        {"currencyCode": "TRY", "totalAmount": Decimal("120.00"), "transactionCount": 1,
         "uncategorizedAmount": Decimal("0.00"), "uncategorizedCount": 0}]}

    assert outcome_for("get_spending_summary", {"period": "LastWeek"}, result).facts == facts(
        ("Dönem", "28 Eylül - 4 Ekim 2026"), ("Toplam harcama", "120,00 TL"), ("İşlem sayısı", "1"))


def test_summary_of_two_currencies_keeps_them_apart():
    result = {"period": THIS_MONTH, "currencies": [
        {"currencyCode": "TRY", "totalAmount": Decimal("735.00"), "transactionCount": 5,
         "uncategorizedAmount": Decimal("0"), "uncategorizedCount": 0},
        {"currencyCode": "USD", "totalAmount": Decimal("10.00"), "transactionCount": 1,
         "uncategorizedAmount": Decimal("0"), "uncategorizedCount": 0}]}

    assert outcome_for("get_spending_summary", {"period": "ThisMonth"}, result).facts == facts(
        ("Dönem", "1-10 Ekim 2026"),
        ("Toplam harcama (TRY)", "735,00 TL"), ("İşlem sayısı (TRY)", "5"),
        ("Toplam harcama (USD)", "10,00 USD"), ("İşlem sayısı (USD)", "1"),
        ("Not", "Farklı para birimleri toplanmaz, ayrı gösterilir."))


def test_summary_without_spending_says_so():
    result = {"period": period("Today", "2026-10-10", "2026-10-10"), "currencies": []}

    assert outcome_for("get_spending_summary", {"period": "Today"}, result).facts == facts(
        ("Dönem", "10 Ekim 2026"), ("Sonuç", "Bu dönemde harcama bulunamadı."))


# Category breakdown (docs/api.md: Ada this month FOOD 590 %80,27, unclassified 145 %19,73)


def test_categories_with_amount_and_share():
    result = {"period": THIS_MONTH, "currencies": [
        {"currencyCode": "TRY", "totalAmount": Decimal("735.00"), "categories": [
            {"categoryCode": "FOOD", "displayName": "Yemek", "amount": Decimal("590.00"), "transactionCount": 4,
             "percentage": Decimal("80.27")},
            {"categoryCode": None, "displayName": "Sınıflandırılmamış", "amount": Decimal("145.00"),
             "transactionCount": 1, "percentage": Decimal("19.73")}]}]}

    assert outcome_for("get_category_breakdown", {"period": "ThisMonth"}, result).facts == facts(
        ("Dönem", "1-10 Ekim 2026"),
        ("Toplam harcama", "735,00 TL"),
        ("Yemek", "590,00 TL (%80,27)"),
        ("Sınıflandırılmamış", "145,00 TL (%19,73)"))


def test_categories_without_spending_say_so():
    result = {"period": THIS_MONTH, "currencies": []}
    assert outcome_for("get_category_breakdown", {"period": "ThisMonth"}, result).facts[-1] == Fact(
        label="Sonuç", text="Bu dönemde harcama bulunamadı.")


# Merchant spending (docs/api.md: Ada "starbucks" last 3 months 930 TRY, 5 purchases)

LAST_3 = period("Last3Months", "2026-07-10", "2026-10-10")


def merchant_result(status: str, merchant=None, partial=False, candidates=(), currencies=()):
    return {"period": LAST_3, "match": {"status": status, "merchant": merchant, "isPartialMatch": partial,
                                        "candidates": list(candidates)}, "currencies": list(currencies)}


STARBUCKS_TOTAL = [{"currencyCode": "TRY", "totalAmount": Decimal("930.00"), "transactionCount": 5}]


def test_matched_merchant():
    result = merchant_result("Matched", "Starbucks", currencies=STARBUCKS_TOTAL)

    assert outcome_for("get_merchant_spending", {"merchant": "Starbucks", "period": "Last3Months"}, result) == ToolOutcome(
        facts(("Dönem", "10 Temmuz - 10 Ekim 2026"), ("İşyeri", "Starbucks"),
              ("Toplam harcama", "930,00 TL"), ("İşlem sayısı", "5")))


def test_partial_match_is_stated():
    result = merchant_result("Matched", "Starbucks", partial=True, currencies=STARBUCKS_TOTAL)

    assert outcome_for("get_merchant_spending", {"merchant": "starbuck", "period": "Last3Months"}, result).facts[2] == Fact(
        label="Not", text="‘starbuck’ yazımı ‘Starbucks’ ile eşleşti.")


def test_known_merchant_without_spending_in_the_period():
    result = merchant_result("Matched", "Starbucks")

    assert outcome_for("get_merchant_spending", {"merchant": "Starbucks", "period": "Today"}, result).facts[-1] == Fact(
        label="Sonuç", text="Bu dönemde bu işyerinde harcama bulunamadı.")


def test_ambiguous_merchant_asks_back_without_the_model():
    result = merchant_result("Ambiguous", candidates=["Şehir Ulaşım", "Şehirlerarası Otobüs"])

    outcome = outcome_for("get_merchant_spending", {"merchant": "şehir", "period": "Last3Months"}, result)

    assert outcome.reply == ("clarification",
                             "‘şehir’ birden fazla işyeriyle eşleşti: Şehir Ulaşım, Şehirlerarası Otobüs. Hangisini kastettiniz?")


def test_unknown_merchant_is_answered_without_the_model():
    result = merchant_result("NotFound")

    outcome = outcome_for("get_merchant_spending", {"merchant": "Migros", "period": "Last3Months"}, result)

    assert outcome.reply == ("answer", "‘Migros’ adında bir işyerinde harcamanız bulunmuyor.")
