from decimal import Decimal

import pytest

from contracts import Fact
from facts import ALTERNATIVES, outcome_for
from formatting import price

# INC-03 PR2 K13-D (a what-if asset: prices, value, difference), K14-B (volatility: the largest fall of the price and the saving's
# own worst moment), K18-C (advice sentences are the backend's, appended by code). Results as the MCP server sends them (Decimal);
# Selin's figures are the PR1 tests': income base 8.000, September 7.228,49 (%90,36), entertainment 2.253,50 → 2.297,38 / 2.222,62 /
# 2.194,89, advice 236,18, gold 3.000,60.


def facts(*pairs):
    return [Fact(label=label, text=text) for label, text in pairs]


def day_range(kind, start, end):
    return {"kind": kind, "startDate": start, "endDate": end, "startUtc": "2026-08-31T21:00:00+00:00", "endUtc": "2026-09-30T21:00:00+00:00"}


SEPTEMBER = day_range("LastMonth", "2026-09-01", "2026-09-30")


@pytest.mark.parametrize(("value", "text"), [
    (Decimal("41.15"), "41,15 TL"), (Decimal("41.1500"), "41,15 TL"), (Decimal("4120.5"), "4.120,50 TL"),
    (Decimal("41.23456"), "41,2346 TL"), (Decimal("41.23454"), "41,2345 TL"), (Decimal("41.2300"), "41,23 TL"),
])
def test_a_price_keeps_up_to_four_decimals_and_at_least_two(value, text):
    assert price(value, "TRY") == text


# --- get_income_share -----------------------------------------------------------------------------------------------------------

def income_row(**changes):
    row = {"currencyCode": "TRY", "totalSpent": Decimal("7228.49"), "incomeTotal": Decimal("24000.00"), "incomeCount": 3,
           "incomeBase": Decimal("8000.00"), "spentShare": Decimal("90.36"), "categories": [
               {"categoryCode": "ENTERTAINMENT", "displayName": "Eğlence", "amount": Decimal("2253.50"), "transactionCount": 7,
                "incomeShare": Decimal("28.17")}]}
    return {**row, **changes}


def income(*rows):
    return {"period": SEPTEMBER, "incomeFrom": "2026-07-01", "incomeTo": "2026-09-30", "incomeMonths": 3,
            "method": "ThreeFullMonthSalaryAverage", "currencies": list(rows)}


def test_income_share_has_the_base_the_total_and_the_share():
    outcome = outcome_for("get_income_share", {"period": "LastMonth"}, income(income_row()))

    assert outcome.facts == facts(
        ("Dönem", "1-30 Eylül 2026"),
        ("Gelir tabanı", "8.000,00 TL (son 3 tam ayın ortalama maaşı: 1 Temmuz - 30 Eylül 2026)"),
        ("Toplam harcama", "7.228,49 TL"),
        ("Gelire oranı", "%90,36"),
        ("Eğlence", "2.253,50 TL (gelire oranı %28,17)"))
    assert outcome.reply is None and outcome.appendix == ()


def test_income_share_without_salary_says_the_share_cannot_be_computed():
    row = income_row(incomeBase=None, spentShare=None, incomeTotal=Decimal("0"), incomeCount=0,
                     categories=[{"categoryCode": "FOOD", "displayName": "Yeme & İçme", "amount": Decimal("500.00"),
                                  "transactionCount": 4, "incomeShare": None}])

    got = dict((f.label, f.text) for f in outcome_for("get_income_share", {"period": "LastMonth"}, income(row)).facts)

    assert got["Gelire oranı"] == "Hesaplanamaz: son 3 ayda maaş kaydı yok."
    assert got["Yeme & İçme"] == "500,00 TL" and "Gelir tabanı" not in got


def test_income_share_lists_five_categories_and_counts_the_rest():
    categories = [{"categoryCode": f"C{n}", "displayName": f"Kategori {n}", "amount": Decimal("100.00"), "transactionCount": 1,
                   "incomeShare": Decimal("1.25")} for n in range(7)]

    got = outcome_for("get_income_share", {"period": "LastMonth"}, income(income_row(categories=categories))).facts

    assert [f.label for f in got].count("Not") == 1
    assert got[-1] == Fact(label="Not", text="Bu dönemde 2 kategori daha var; ilk 5 kategori gösterildi.")


def test_income_share_of_two_currencies_keeps_them_apart():
    got = outcome_for("get_income_share", {"period": "LastMonth"}, income(income_row(), income_row(currencyCode="USD"))).facts

    assert Fact(label="Toplam harcama (TRY)", text="7.228,49 TL") in got and Fact(label="Toplam harcama (USD)", text="7.228,49 USD") in got
    assert got[-1].text == "Farklı para birimleri toplanmaz, ayrı gösterilir."


def test_income_share_of_a_month_without_spending_says_so():
    assert outcome_for("get_income_share", {"period": "LastMonth"}, income()).facts == facts(
        ("Dönem", "1-30 Eylül 2026"), ("Sonuç", "Bu dönemde harcama bulunamadı."))


# --- get_spending_advice --------------------------------------------------------------------------------------------------------

ADVICE_SENTENCE = "Eylül ayında Eğlence harcamanız olağan ayınızın 2,43 katı; %20 azaltırsanız ayda 236,18 TL biriktirirsiniz."


def advice_row(**changes):
    row = {"currencyCode": "TRY", "totalSpent": Decimal("7228.49"), "incomeTotal": Decimal("24000.00"), "incomeBase": Decimal("8000.00"),
           "spentShare": Decimal("90.36"), "reason": None, "categories": [], "groups": [
               {"group": "Essential", "amount": Decimal("3000.00"), "incomeShare": Decimal("37.50")},
               {"group": "Discretionary", "amount": Decimal("2253.50"), "incomeShare": Decimal("28.17")},
               {"group": "Unknown", "amount": Decimal("1974.99"), "incomeShare": Decimal("24.69")}],
           "advice": [{"rule": "CategoryAboveUsual", "categoryCode": "ENTERTAINMENT", "monthlySaving": Decimal("236.18"),
                       "annualSaving": Decimal("2834.20"), "message": ADVICE_SENTENCE}]}
    return {**row, **changes}


def advice(*rows):
    return {"period": SEPTEMBER, "thresholds": {"discretionaryLimitPercent": 30, "maximumAdvice": 2}, "method": "RuleBasedAdvice",
            "currencies": list(rows)}


def test_advice_gives_the_groups_and_appends_the_backends_sentences():
    outcome = outcome_for("get_spending_advice", {"period": "LastMonth"}, advice(advice_row()))

    assert outcome.facts == facts(
        ("Dönem", "1-30 Eylül 2026"),
        ("Gelir tabanı", "8.000,00 TL"),
        ("Toplam harcama", "7.228,49 TL"),
        ("Temel harcama", "3.000,00 TL (gelire oranı %37,50)"),
        ("İsteğe bağlı harcama", "2.253,50 TL (gelire oranı %28,17)"),
        ("Diğer ve sınıflandırılmamış harcama", "1.974,99 TL (gelire oranı %24,69)"),
        ("Öneriler", "1 öneri aşağıda ayrıca yazılıyor."))
    # K18-C: the sentence is the backend's own, put under the answer by code, never retyped by the model.
    assert outcome.appendix == (ADVICE_SENTENCE,)


def test_advice_that_no_rule_gave_is_stated_with_the_limit():
    outcome = outcome_for("get_spending_advice", {"period": "LastMonth"}, advice(advice_row(advice=[], reason="NoRuleMatched")))

    assert outcome.facts[-1] == Fact(
        label="Öneri", text="Yok: isteğe bağlı harcama gelirin %30 sınırının altında ve hiçbir kategori olağan ayından belirgin ölçüde sapmadı.")
    assert outcome.appendix == ()


def test_advice_without_salary_says_it_cannot_compare_with_income():
    row = advice_row(advice=[], reason="NoIncome", incomeBase=None, spentShare=None, groups=[])

    assert outcome_for("get_spending_advice", {"period": "LastMonth"}, advice(row)).facts == facts(
        ("Dönem", "1-30 Eylül 2026"), ("Toplam harcama", "7.228,49 TL"),
        ("Sonuç", "Son 3 ayda maaş kaydı olmadığı için gelire göre öneri verilemez."))


# --- what_if_one_off ------------------------------------------------------------------------------------------------------------

DISCLAIMER = "Geçmiş fiyatlarla yapılmış bir hesaptır; gelecekteki getiriyi göstermez ve yatırım tavsiyesi değildir."


def asset(name, unit, buy, now, value, difference, percent, status="Ok", **extra):
    return {"symbol": unit, "name": name, "baseUnit": unit, "buyPrice": buy, "valuePrice": now, "value": value,
            "difference": difference, "differencePercent": percent, "status": status, **extra}


def one_off(assets, spent=Decimal("2253.50"), category=("ENTERTAINMENT", "Eğlence")):
    return {"period": SEPTEMBER, "categoryCode": category[0], "categoryDisplayName": category[1], "currencyCode": "TRY",
            "spentAmount": spent, "buyDate": "2026-09-01", "valueDate": "2026-10-10", "method": "SingleBuyAtPeriodStart",
            "disclaimer": DISCLAIMER, "assets": assets}


THREE = [asset("Dolar", "USD", Decimal("41.15"), Decimal("41.95"), Decimal("2297.38"), Decimal("43.88"), Decimal("1.95")),
         asset("Euro", "EUR", Decimal("48.2"), Decimal("47.54"), Decimal("2222.62"), Decimal("-30.88"), Decimal("-1.37")),
         asset("Gram altın", "GRAM", Decimal("4120.123456"), Decimal("4013.0"), Decimal("2194.89"), Decimal("-58.61"), Decimal("-2.60"))]


def test_one_off_shows_the_prices_the_value_and_the_difference_of_each_asset():
    outcome = outcome_for("what_if_one_off", {"period": "LastMonth", "categoryCode": "ENTERTAINMENT"}, one_off(THREE))

    assert outcome.facts == facts(
        ("Dönem", "1-30 Eylül 2026"), ("Kategori", "Eğlence"), ("Harcanan", "2.253,50 TL"),
        ("Alış günü", "1 Eylül 2026"), ("Değerleme günü", "10 Ekim 2026"),
        ("Dolar alsaydınız", "alışta 41,15 TL, bugün 41,95 TL (USD başına); değeri 2.297,38 TL; 43,88 TL artış (%1,95)"),
        ("Euro alsaydınız", "alışta 48,20 TL, bugün 47,54 TL (EUR başına); değeri 2.222,62 TL; 30,88 TL azalış (%1,37)"),
        ("Gram altın alsaydınız", "alışta 4.120,1235 TL, bugün 4.013,00 TL (gram başına); değeri 2.194,89 TL; 58,61 TL azalış (%2,60)"),
        ("Not", ALTERNATIVES))
    # K6-A: the backend's fixed warning goes under the answer, written by code.
    assert outcome.appendix == (DISCLAIMER,)


def test_one_off_without_a_category_has_no_category_line():
    got = outcome_for("what_if_one_off", {"period": "LastMonth"}, one_off(THREE, category=(None, None))).facts

    assert "Kategori" not in [f.label for f in got]


def test_one_off_names_an_asset_whose_price_is_missing_or_too_old():
    stale = asset("Gram altın", "GRAM", None, None, None, None, None, status="Stale")

    got = outcome_for("what_if_one_off", {"period": "LastMonth"}, one_off([THREE[0], stale])).facts

    assert Fact(label="Gram altın alsaydınız", text="fiyat bulunamadı ya da çok eski; hesaplanamadı") in got


def test_one_off_without_lira_spending_is_a_fixed_answer():
    outcome = outcome_for("what_if_one_off", {"period": "LastMonth"}, one_off([], spent=Decimal("0")))

    assert outcome.reply == ("answer", "Bu dönemde TL harcamanız bulunmadığı için hesaplanamadı.") and outcome.facts == []


# --- what_if_monthly ------------------------------------------------------------------------------------------------------------

def monthly(source="Advice", amount=Decimal("236.18"), status="Used", assets=None):
    return {"amountSource": source, "adviceStatus": status, "advicePeriod": SEPTEMBER if source == "Advice" else None,
            "currencyCode": "TRY", "amount": amount, "months": 12, "firstBuyDate": "2025-10-01", "lastBuyDate": "2026-09-01",
            "valueDate": "2026-10-10", "invested": None if amount is None else amount * 12, "method": "MonthlyBuyOnFirstDay",
            "disclaimer": DISCLAIMER, "assets": assets or []}


GOLD = asset("Gram altın", "GRAM", None, None, Decimal("3000.60"), Decimal("166.44"), Decimal("5.87"), invested=Decimal("2834.16"),
             volatility={"maxDrawdown": {"percent": Decimal("-17.97"), "peakDate": "2026-01-01", "troughDate": "2026-05-01"},
                         "worstMoment": {"date": "2026-05-01", "invested": Decimal("1180.90"), "value": Decimal("1142.64"),
                                         "returnPercent": Decimal("-3.24")}})


def test_monthly_from_advice_shows_both_views_of_the_volatility():
    outcome = outcome_for("what_if_monthly", {"fromAdvice": True}, monthly(assets=[GOLD]))

    assert outcome.facts == facts(
        ("Aylık tutar", "236,18 TL (Eylül 2026 tasarruf önerisi)"),
        ("Alımlar", "12 ay, her ayın 1'i: 1 Ekim 2025 - 1 Eylül 2026"),
        ("Değerleme günü", "10 Ekim 2026"),
        ("Toplam yatırılan", "2.834,16 TL"),
        ("Her ay gram altın alsaydınız", "değeri 3.000,60 TL; 166,44 TL artış (%5,87)"),
        ("Gram altın: en büyük fiyat düşüşü", "%17,97 (1 Ocak 2026 - 1 Mayıs 2026)"),
        ("Gram altın: birikimin en kötü anı", "1 Mayıs 2026; yatırılan 1.180,90 TL, değer 1.142,64 TL (-%3,24)"),
        ("Not", ALTERNATIVES))
    assert outcome.appendix == (DISCLAIMER,)


def test_monthly_from_the_users_amount_says_whose_amount_it_is():
    got = outcome_for("what_if_monthly", {"amount": 1000, "currencyCode": "TRY"}, monthly("User", Decimal("1000.00"))).facts

    assert got[0] == Fact(label="Aylık tutar", text="1.000,00 TL (sizin söylediğiniz tutar)")


def test_monthly_whose_price_never_fell_says_so():
    calm = {**GOLD, "volatility": {**GOLD["volatility"], "maxDrawdown": {"percent": Decimal("0"), "peakDate": None, "troughDate": None}}}

    got = dict((f.label, f.text) for f in outcome_for("what_if_monthly", {"fromAdvice": True}, monthly(assets=[calm])).facts)

    assert got["Gram altın: en büyük fiyat düşüşü"] == "Fiyat dönem boyunca önceki bir noktanın altına düşmedi."


def test_monthly_asset_without_figures_is_named_and_has_no_volatility():
    missing = asset("Gram altın", "GRAM", None, None, None, None, None, status="Missing", invested=Decimal("2834.16"), volatility=None)

    got = outcome_for("what_if_monthly", {"fromAdvice": True}, monthly(assets=[missing])).facts

    assert Fact(label="Her ay gram altın alsaydınız", text="fiyat bulunamadı ya da çok eski; hesaplanamadı") in got


def test_monthly_without_advice_is_a_fixed_answer_that_invites_the_users_own_amount():
    outcome = outcome_for("what_if_monthly", {"fromAdvice": True}, monthly(amount=None, status="NoAdvice"))

    assert outcome.reply is not None and outcome.reply[0] == "answer" and outcome.facts == []
    assert "Ayda 1.000 TL biriktirseydim" in outcome.reply[1] and "tasarruf önerisi çıkmadığı" in outcome.reply[1]


# --- CHAT-09 PR2 K7-A, K16-A: the question the explainer is given, and which value is whose -----------------------------------

def test_one_off_gives_the_explainer_a_question_written_by_code():
    # The simulator's explainer read "Geçen ayki ... nolurdu (5000) (Tl)" and wrote "Geçen ay 5.000 TL biriktirseydiniz".
    outcome = outcome_for("what_if_one_off", {"period": "LastMonth", "categoryCode": "ENTERTAINMENT"}, one_off(THREE))

    assert outcome.question == "1-30 Eylül 2026 dönemindeki eğlence harcamamla dolar, euro ya da gram altın alsaydım bugün ne olurdu?"
    assert outcome.named_values == (("dolar", "2.297,38"), ("euro", "2.222,62"), ("altin", "2.194,89"))


def test_one_off_without_a_category_or_with_a_missing_price_names_only_what_is_there():
    stale = asset("Gram altın", "GRAM", None, None, None, None, None, status="Stale")

    outcome = outcome_for("what_if_one_off", {"period": "LastMonth"}, one_off([THREE[0], stale], category=(None, None)))

    assert outcome.question == "1-30 Eylül 2026 dönemindeki harcamamla dolar ya da gram altın alsaydım bugün ne olurdu?"
    assert outcome.named_values == (("dolar", "2.297,38"),)


def test_monthly_gives_the_explainer_the_months_and_the_amount():
    outcome = outcome_for("what_if_monthly", {"fromAdvice": True}, monthly(assets=[GOLD]))

    assert outcome.question == "Son 12 ay boyunca her ay 236,18 TL ile gram altın alsaydım bugün ne olurdu?"
    assert outcome.named_values == (("altin", "3.000,60"),)


def test_the_other_analyses_keep_the_users_question():
    assert outcome_for("get_income_share", {"period": "LastMonth"}, income(income_row())).question is None
