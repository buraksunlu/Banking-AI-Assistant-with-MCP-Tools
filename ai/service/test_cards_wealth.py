"""The wealth cards (UX-01 PR6c): the income share, the advice and the two what-ifs. Selin's, Zeynep's, Can's and Ada's results at
the demo moment (MCP server, 16 Nov 2026), the same as the answer eval's A19-A24."""

from decimal import Decimal

from answer_check import check_answer
from cards import Bar, Block, Entry, Headline, Stat, card_texts, cards_for
from facts import ALTERNATIVES_LINE, outcome_for
from test_cards import SEPTEMBER

D = Decimal
DISCLAIMER = ("Geçmiş fiyatlarla yapılmış bir hesaptır; gelecekteki getiriyi göstermez ve yatırım tavsiyesi değildir. Alış-satış farkı, "
              "vergi, komisyon, faiz ve enflasyon dahil değildir.")


def category(code, name, amount, share):
    return {"categoryCode": code, "displayName": name, "amount": D(amount), "transactionCount": 1,
            "incomeShare": None if share is None else D(share)}


SELIN_CATEGORIES = [category("FOOD", "Yeme & İçme", "3746.50", "46.83"), category("ENTERTAINMENT", "Eğlence", "2253.50", "28.17"),
                    category("OTHER", "Diğer", "480.00", "6"), category("BILLS", "Faturalar", "350.00", "4.38"),
                    category("TRANSPORTATION", "Ulaşım", "338.50", "4.23"), category("SUBSCRIPTION", "Abonelik", "59.99", "0.75")]
SELIN_INCOME = {"period": SEPTEMBER, "incomeFrom": "2026-07-01", "incomeTo": "2026-09-30", "incomeMonths": 3, "currencies": [
    {"currencyCode": "TRY", "totalSpent": D("7228.49"), "incomeTotal": D("24000.00"), "incomeCount": 3, "incomeBase": D("8000.00"),
     "spentShare": D("90.36"), "categories": SELIN_CATEGORIES}]}
LAST_MONTH = {"period": "LastMonth"}


def test_the_income_card_is_the_share_with_the_categories_against_the_income():
    card = cards_for("get_income_share", LAST_MONTH, SELIN_INCOME)[0]

    # K7-B: the big figure is a share, not money.
    assert (card.title, card.headline) == ("Gelir payı · Eylül 2026", Headline(None, None, "%90,36"))
    stats, bars = card.simple
    assert stats == Block("stats", items=(Stat("Harcama", "7.228,49 TL"), Stat("Gelir tabanı", "8.000,00 TL")))
    assert bars == Block("bars", rows=(Bar("Yeme & İçme", D("3746.50"), "3.746,50 TL · %46,83", D("1.00")),
                                       Bar("Eğlence", D("2253.50"), "2.253,50 TL · %28,17", D("0.60")),
                                       Bar("Diğer", D("480.00"), "480,00 TL · %6,00", D("0.13"))))
    # The sixth category is not in the facts (LIST_LIMIT), so not on the card.
    assert [bar.label for bar in card.detailed[0].rows] == ["Faturalar", "Ulaşım"]
    assert card.detailed[1] == Block("stats", items=(Stat("Gelir dönemi", "son 3 tam ayın ortalama maaşı: 1 Temmuz - 30 Eylül 2026"),))
    assert card.sentence == "Gelirinizin %90,36 kadarını harcadınız."


def test_without_a_salary_the_income_share_has_no_card():
    # K8-A: Can has no salary; the text answer says the share cannot be computed.
    can = {**SELIN_INCOME, "currencies": [{**SELIN_INCOME["currencies"][0], "incomeBase": None, "spentShare": None, "incomeTotal": 0,
                                           "categories": [category("GROCERY", "Market", "7348.00", None)]}]}

    assert cards_for("get_income_share", LAST_MONTH, can) == []


def test_two_currencies_of_income_are_two_cards():
    zeynep = {**SELIN_INCOME, "currencies": [
        {"currencyCode": "TRY", "totalSpent": D("10490.00"), "incomeBase": D("24333.33"), "spentShare": D("43.11"),
         "categories": [category("GROCERY", "Market", "4105.50", "16.87")]},
        {"currencyCode": "USD", "totalSpent": D("37.99"), "incomeBase": D("1350.00"), "spentShare": D("2.81"),
         "categories": [category("SUBSCRIPTION", "Abonelik", "37.99", "2.81")]}]}

    cards = cards_for("get_income_share", LAST_MONTH, zeynep)

    assert [(c.title, c.headline.text) for c in cards] == [("Gelir payı · Eylül 2026 (TRY)", "%43,11"),
                                                           ("Gelir payı · Eylül 2026 (USD)", "%2,81")]


ADVICE_MESSAGE = ("Eylül 2026 ayında Eğlence harcamanız 2.253,50 TL oldu; önceki 6 ayın aylık ortalaması 927,83 TL (2,43 katı). Eğlence "
                  "harcamasını %20 azaltırsanız son 6 ayın ortalamasıyla ayda 236,18 TL, yılda 2.834,20 TL kalır.")
SELIN_ADVICE = {"period": SEPTEMBER, "thresholds": {"discretionaryLimitPercent": 30}, "currencies": [
    {"currencyCode": "TRY", "reason": None, "totalSpent": D("7228.49"), "incomeBase": D("8000.00"), "spentShare": D("90.36"),
     "groups": [{"group": "Essential", "amount": D("4435.00"), "incomeShare": D("55.44")},
                {"group": "Discretionary", "amount": D("2313.49"), "incomeShare": D("28.92")},
                {"group": "Unknown", "amount": D("480.00"), "incomeShare": D("6")}],
     "advice": [{"message": ADVICE_MESSAGE}]}]}


def test_the_advice_card_is_the_groups_against_the_income_and_the_backends_advice_as_a_note():
    card = cards_for("get_spending_advice", LAST_MONTH, SELIN_ADVICE)[0]

    assert (card.title, card.headline.text) == ("Harcama önerisi · Eylül 2026", "7.228,49 TL")
    assert card.simple == (Block("bars", rows=(
        Bar("Temel harcama", D("4435.00"), "4.435,00 TL · gelirin %55,44", D("1.00")),
        Bar("İsteğe bağlı harcama", D("2313.49"), "2.313,49 TL · gelirin %28,92", D("0.52")),
        Bar("Diğer ve sınıflandırılmamış harcama", D("480.00"), "480,00 TL · gelirin %6,00", D("0.11")))),)
    # K18-C: the backend's own sentence, as written; always shown (P3-A).
    assert (card.sentence, card.notes) == ("İsteğe bağlı harcama gelirin %28,92 kadarı.", (ADVICE_MESSAGE,))


def test_no_advice_says_why_in_its_note_and_no_salary_has_no_card():
    calm = {**SELIN_ADVICE, "currencies": [{**SELIN_ADVICE["currencies"][0], "advice": []}]}
    can = {**SELIN_ADVICE, "currencies": [{**SELIN_ADVICE["currencies"][0], "reason": "NoIncome", "incomeBase": None}]}

    assert cards_for("get_spending_advice", LAST_MONTH, calm)[0].notes == (
        "Yok: isteğe bağlı harcama gelirin %30 sınırının altında ve hiçbir kategori olağan ayından belirgin ölçüde sapmadı.",)
    assert cards_for("get_spending_advice", LAST_MONTH, can) == []


def asset(unit, value, difference, rate, volatility=None):
    priced = value is not None
    return {"baseUnit": unit, "name": unit, "value": D(value) if priced else None, "difference": D(difference) if priced else None,
            "differencePercent": D(rate) if priced else None, "buyPrice": D("1"), "valuePrice": D("1"), "volatility": volatility}


SELIN_ONE_OFF = {"period": SEPTEMBER, "categoryCode": "ENTERTAINMENT", "categoryDisplayName": "Eğlence", "currencyCode": "TRY",
                 "spentAmount": D("2253.50"), "buyDate": "2026-09-01", "valueDate": "2026-10-10", "disclaimer": DISCLAIMER,
                 "assets": [asset("USD", "2297.38", "43.88", "1.95"), asset("EUR", "2222.62", "-30.88", "-1.37"),
                            asset("GRAM", "2194.89", "-58.61", "-2.60")]}
ONE_OFF_ARGS = {"period": "LastMonth", "categoryCode": "ENTERTAINMENT"}


def test_the_one_off_card_is_each_asset_as_an_alternative_with_the_warning():
    card = cards_for("what_if_one_off", ONE_OFF_ARGS, SELIN_ONE_OFF)[0]

    assert (card.title, card.headline.text) == ("Ne olurdu · Eğlence · Eylül 2026", "2.253,50 TL")
    assert card.simple == (Block("list", entries=(Entry("Dolar", "43,88 TL artış · %1,95", "2.297,38 TL", "up"),
                                                  Entry("Euro", "30,88 TL azalış · %1,37", "2.222,62 TL", "down"),
                                                  Entry("Gram altın", "58,61 TL azalış · %2,60", "2.194,89 TL", "down"))),)
    assert card.detailed == (Block("stats", items=(Stat("Alış günü", "1 Eylül 2026"), Stat("Değerleme günü", "10 Ekim 2026"))),)
    assert card.sentence == "Alış 1 Eylül 2026, değerleme 10 Ekim 2026."
    # INC-03 K6-A: the warning is never hidden; three priced assets are alternatives, never a sum.
    assert card.notes == (DISCLAIMER, ALTERNATIVES_LINE)


def test_an_asset_without_a_price_stays_on_the_card_as_not_computed():
    # K8-C: hiding it would read as if there were only two choices.
    one_priced = {**SELIN_ONE_OFF, "assets": [asset("USD", "2297.38", "43.88", "1.95"), asset("GRAM", None, None, None)]}

    card = cards_for("what_if_one_off", ONE_OFF_ARGS, one_priced)[0]

    assert card.simple[0].entries[1] == Entry("Gram altın", "fiyat bulunamadı ya da çok eski", "hesaplanamadı")
    assert card.notes == (DISCLAIMER,)


FALL = {"maxDrawdown": {"percent": D("-17.97"), "peakDate": "2026-02-27", "troughDate": "2026-07-01"},
        "worstMoment": {"date": "2026-07-01", "invested": 9000, "value": D("8708.11"), "returnPercent": D("-3.24")}}
SELIN_MONTHLY = {"amountSource": "User", "adviceStatus": None, "advicePeriod": None, "currencyCode": "TRY", "amount": 1000, "months": 12,
                 "firstBuyDate": "2025-10-01", "lastBuyDate": "2026-09-01", "valueDate": "2026-10-10", "invested": 12000,
                 "disclaimer": DISCLAIMER,
                 "assets": [asset("USD", "13298.92", "1298.92", "10.82"), asset("EUR", "12800.85", "800.85", "6.67"),
                            asset("GRAM", "12704.72", "704.72", "5.87", FALL)]}
MONTHLY_ARGS = {"amount": 1000, "currencyCode": "TRY", "months": 12}


def test_the_monthly_card_is_what_was_put_in_and_each_assets_value_with_its_risk_in_the_detail():
    card = cards_for("what_if_monthly", MONTHLY_ARGS, SELIN_MONTHLY)[0]

    assert (card.title, card.headline.text) == ("Aylık birikim · 1.000,00 TL × 12 ay", "12.000,00 TL")
    assert [(e.label, e.text, e.direction) for e in card.simple[0].entries] == [
        ("Dolar", "13.298,92 TL", "up"), ("Euro", "12.800,85 TL", "up"), ("Gram altın", "12.704,72 TL", "up")]
    assert card.detailed[0].items == (
        Stat("Alımlar", "12 ay, her ayın 1'i: 1 Ekim 2025 - 1 Eylül 2026"),
        Stat("Gram altın: en büyük fiyat düşüşü", "%17,97 (27 Şubat 2026 - 1 Temmuz 2026)"),
        Stat("Gram altın: birikimin en kötü anı", "1 Temmuz 2026; yatırılan 9.000,00 TL, değer 8.708,11 TL (-%3,24)"))
    assert card.sentence == "Toplam 12.000,00 TL yatırmış olurdunuz; değerleme 10 Ekim 2026."
    assert card.notes == (DISCLAIMER, ALTERNATIVES_LINE)


def test_no_advised_amount_and_no_lira_spending_have_no_card():
    assert cards_for("what_if_monthly", {"fromAdvice": True, "months": 6}, {**SELIN_MONTHLY, "adviceStatus": "NoAdvice"}) == []
    assert cards_for("what_if_one_off", ONE_OFF_ARGS, {**SELIN_ONE_OFF, "assets": []}) == []


def test_every_text_of_the_wealth_cards_is_in_their_facts_or_the_backends_own_sentences():
    # K6-A: what the card says is in the facts, or in the backend's sentences the answer carries under it (the advice, the warning).
    for tool, arguments, result in [("get_income_share", LAST_MONTH, SELIN_INCOME), ("get_spending_advice", LAST_MONTH, SELIN_ADVICE),
                                    ("what_if_one_off", ONE_OFF_ARGS, SELIN_ONE_OFF), ("what_if_monthly", MONTHLY_ARGS, SELIN_MONTHLY)]:
        outcome = outcome_for(tool, arguments, result)
        for card in cards_for(tool, arguments, result):
            checked = check_answer("\n".join(outcome.appendix), outcome.facts, "\n".join(card_texts(card)))
            assert checked.invented_numbers == (), (tool, card.title)
