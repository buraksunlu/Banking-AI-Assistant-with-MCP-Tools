"""The planning cards (UX-01 PR6b): the budget and the saving. Murat's, Selin's, Elif's and Zeynep's results at the demo moment
(MCP server, 16 Nov 2026), the same as the answer eval's A11-A15."""

from decimal import Decimal

from answer_check import check_answer
from cards import Bar, Block, Point, Stat, card_texts, cards_for
from facts import BUDGET_RULE, SAVINGS_RULE, outcome_for
from test_cards import period

D = Decimal
OCTOBER = period("ThisMonth", "2026-10-01", "2026-10-10")


def budget(spent, count, budget_amount, remaining, over, is_over, average, projected, projected_over, will_exceed, currency="TRY",
           category=("GROCERY", "Market")):
    return {"period": OCTOBER, "currencyCode": currency, "budgetAmount": budget_amount, "categoryCode": category[0] if category else None,
            "categoryDisplayName": category[1] if category else None, "merchantMatch": None,
            "figures": {"spentAmount": D(spent), "transactionCount": count, "remainingAmount": D(remaining), "overAmount": D(over),
                        "isOverBudget": is_over, "elapsedDays": 10, "daysInMonth": 31, "dailyAverage": D(average),
                        "projectedMonthEnd": D(projected), "projectedOverAmount": D(projected_over), "willExceed": will_exceed}}


MURAT_BUDGET = budget("5133.00", 2, 8000, "2867.00", "0", False, "513.30", "15912.30", "7912.30", True)
MURAT_ARGS = {"amount": 8000, "currencyCode": "TRY", "categoryCode": "GROCERY"}


def test_the_budget_card_is_today_and_the_month_end_against_the_budget():
    card = cards_for("check_budget", MURAT_ARGS, MURAT_BUDGET)[0]

    assert (card.title, card.headline.text) == ("Market bütçesi · 1-10 Ekim 2026", "5.133,00 TL")
    # K6-C: each bar is its figure against the budget, for drawing only, never longer than full.
    assert card.simple[0] == Block("bars", rows=(
        Bar("Bugün", D("5133.00"), "5.133,00 TL / 8.000,00 TL", D("0.64")),
        Bar("Bu hızla ay sonu", D("15912.30"), "15.912,30 TL", D("1.00"), "up")))
    assert card.simple[1] == Block("stats", items=(Stat("Kalan", "2.867,00 TL"), Stat("Tahmini aşım", "7.912,30 TL")))
    assert card.detailed == (Block("stats", items=(Stat("Günlük ortalama", "513,30 TL"), Stat("İşlem", "2"),
                                                   Stat("Gün", "ayın 10. günü; ay 31 gün"))),)
    assert (card.sentence, card.notes) == ("Bu hızla bütçeyi 7.912,30 TL aşar.", (BUDGET_RULE,))


def test_a_budget_already_over_says_so_and_how_far_it_goes():
    elif_budget = budget("1555.03", 4, 1500, "0", "55.03", True, "155.50", "4820.59", "3320.59", True, "EUR", None)

    card = cards_for("check_budget", {"amount": 1500, "currencyCode": "EUR"}, elif_budget)[0]

    assert card.title == "Bütçe · 1-10 Ekim 2026"
    assert card.simple[0].rows[0].share == D("1.00")
    assert card.simple[1].items[0] == Stat("Bütçe aşımı", "55,03 EUR")
    assert card.sentence == "Bütçeyi şimdiden 55,03 EUR aştınız; bu hızla ay sonunda 3.320,59 EUR aşar."


def test_a_budget_within_reach_has_no_overrun_and_no_arrow():
    calm = budget("1000.00", 3, 8000, "7000.00", "0", False, "100.00", "3100.00", "0", False)

    card = cards_for("check_budget", MURAT_ARGS, calm)[0]

    assert card.simple[0].rows[1] == Bar("Bu hızla ay sonu", D("3100.00"), "3.100,00 TL", D("0.39"))
    assert card.simple[1].items == (Stat("Kalan", "7.000,00 TL"),)
    assert card.sentence == "Bu hızla bütçenin içinde kalır."


def test_a_merchant_budget_is_named_after_the_merchant_and_an_unmatched_one_has_no_card():
    starbucks = {**MURAT_BUDGET, "categoryCode": None, "categoryDisplayName": None,
                 "merchantMatch": {"status": "Matched", "merchant": "Starbucks", "isPartialMatch": False, "candidates": []}}
    unknown = {**starbucks, "merchantMatch": {"status": "NotFound", "merchant": None, "isPartialMatch": False, "candidates": []}}
    arguments = {"amount": 8000, "currencyCode": "TRY", "merchant": "Starbucks"}

    assert cards_for("check_budget", arguments, starbucks)[0].title == "Starbucks bütçesi · 1-10 Ekim 2026"
    assert cards_for("check_budget", arguments, unknown) == []


def month_amount(number, amount):
    return {"year": 2026, "month": number, "isPartial": False, "amount": D(amount), "transactionCount": 1}


def saving(currency, total, average, monthly, annual, amounts):
    return {"currencyCode": currency, "totalSpent": D(total), "monthlyAverage": D(average), "monthlySaving": D(monthly),
            "annualSaving": D(annual), "monthlyAmounts": [month_amount(m, a) for m, a in amounts]}


SELIN_SAVING = {"from": "2026-04-01", "to": "2026-09-30", "months": 6, "reductionPercent": 20, "categoryCode": "FOOD",
                "categoryDisplayName": "Yeme & İçme", "merchantMatch": None,
                "currencies": [saving("TRY", "19125.00", "3187.50", "637.50", "7650.00",
                                      [(4, "3316.00"), (5, "3879.00"), (6, "4403.50"), (7, "3780.00"), (8, "0"), (9, "3746.50")])]}
SELIN_ARGS = {"reductionPercent": 20, "categoryCode": "FOOD"}


def test_the_saving_card_is_the_monthly_saving_with_the_year_and_the_months_behind_it():
    card = cards_for("simulate_savings", SELIN_ARGS, SELIN_SAVING)[0]

    assert (card.title, card.headline.text) == ("Yeme & İçme tasarrufu %20 · 1 Nisan - 30 Eylül 2026", "637,50 TL")
    assert card.simple == (Block("stats", items=(Stat("Yılda", "7.650,00 TL"), Stat("Aylık ortalama", "3.187,50 TL"))),)
    series, stats = card.detailed
    # Every month, an empty one as 0 (ANA-14 K2-A): the average divides by all six.
    assert series.points[4] == Point("Ağu", D("0"), "0,00 TL", D("0.00"), False)
    assert series.points[2] == Point("Haz", D("4403.50"), "4.403,50 TL", D("1.00"), False)
    assert stats == Block("stats", items=(Stat("Toplam harcama", "19.125,00 TL"), Stat("Dönem", "6 tam ay")))
    assert (card.sentence, card.notes) == ("%20 azaltırsanız ayda 637,50 TL, yılda 7.650,00 TL.", (SAVINGS_RULE,))


def test_two_currencies_of_saving_are_two_cards():
    zeynep = {**SELIN_SAVING, "reductionPercent": 10, "categoryCode": None, "categoryDisplayName": None,
              "currencies": [saving("TRY", "63594.00", "10599.00", "1059.90", "12718.80", [(4, "10524.00"), (9, "10490.00")]),
                             saving("USD", "227.94", "37.99", "3.80", "45.59", [(4, "37.99"), (9, "37.99")])]}

    cards = cards_for("simulate_savings", {"reductionPercent": 10}, zeynep)

    assert [(c.title, c.headline.text) for c in cards] == [("Tasarruf %10 · 1 Nisan - 30 Eylül 2026 (TRY)", "1.059,90 TL"),
                                                           ("Tasarruf %10 · 1 Nisan - 30 Eylül 2026 (USD)", "3,80 USD")]


def test_every_text_of_the_planning_cards_is_in_their_facts():
    for tool, arguments, result in [("check_budget", MURAT_ARGS, MURAT_BUDGET), ("simulate_savings", SELIN_ARGS, SELIN_SAVING)]:
        facts = outcome_for(tool, arguments, result).facts
        for card in cards_for(tool, arguments, result):
            assert check_answer("", facts, "\n".join(card_texts(card))).invented_numbers == (), (tool, card.title)


def test_a_saving_for_a_merchant_that_did_not_match_has_no_card():
    # The text answer asks back or names the miss; a card would show figures of no merchant the user meant.
    ambiguous = {**SELIN_SAVING, "categoryCode": None, "categoryDisplayName": None,
                 "merchantMatch": {"status": "Ambiguous", "merchant": None, "isPartialMatch": False, "candidates": ["A", "B"]}}

    assert cards_for("simulate_savings", {"reductionPercent": 20, "merchant": "Migros"}, ambiguous) == []
