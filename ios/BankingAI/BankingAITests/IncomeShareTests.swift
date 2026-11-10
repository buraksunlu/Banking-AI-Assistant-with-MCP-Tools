import Foundation
import Testing
@testable import BankingAI

// INC-04: the home card "Gelire göre harcama" shows `GET /api/analytics/spending-advice?period=LastMonth` as it comes. Figures
// are the backend's (docs/analytics.md INC-01/INC-02, demo moment 10 October 2026, September 2026); the phone only writes them
// and draws the bar.

// Selin: 7.228,49 of an 8.000 income base (90,36 %), one piece of advice on Eğlence (docs/analytics.md INC-02).
private let selin = """
{
  "period": { "kind": "LastMonth", "startDate": "2026-09-01", "endDate": "2026-09-30" },
  "incomeFrom": "2026-07-01", "incomeTo": "2026-09-30", "usualFrom": "2026-03-01", "usualTo": "2026-08-31",
  "savingsFrom": "2026-04-01", "savingsTo": "2026-09-30",
  "thresholds": { "discretionaryLimitPercent": 30, "usualRatio": 1.5, "minimumImpactPercent": 2, "usualMonths": 6,
                  "reductionPercent": 20, "maximumAdvice": 2 },
  "method": "RuleBasedAdvice",
  "currencies": [ {
    "currencyCode": "TRY", "totalSpent": 7228.49, "incomeTotal": 24000.00, "incomeBase": 8000.00, "spentShare": 90.36,
    "groups": [ { "group": "Essential", "amount": 4435.00, "incomeShare": 55.44 },
                { "group": "Discretionary", "amount": 2313.49, "incomeShare": 28.92 },
                { "group": "Unknown", "amount": 480.00, "incomeShare": 6.00 } ],
    "categories": [ { "categoryCode": "ENTERTAINMENT", "displayName": "Eğlence", "group": "Discretionary", "amount": 2253.50,
                      "incomeShare": 28.17, "usualMonthlyAmount": 927.83 } ],
    "advice": [ { "rule": "CategoryAboveUsual", "categoryCode": "ENTERTAINMENT", "categoryDisplayName": "Eğlence",
                  "amount": 2253.50, "reference": 927.83, "ratio": 2.43, "excess": 1325.67, "excessShare": 16.57,
                  "reductionPercent": 20, "monthlySaving": 236.18, "annualSaving": 2834.20,
                  "message": "Eylül 2026 ayında Eğlence harcamanız 2.253,50 TL oldu." } ],
    "reason": null } ]
}
"""

// Murat: 62,81 % of income and no advice (the school instalment is essential). The group split is illustrative; only the total
// share is from the docs.
private let murat = """
{
  "period": { "kind": "LastMonth", "startDate": "2026-09-01", "endDate": "2026-09-30" },
  "currencies": [ {
    "currencyCode": "TRY", "totalSpent": 53388.39, "incomeTotal": 255000.00, "incomeBase": 85000.00, "spentShare": 62.81,
    "groups": [ { "group": "Essential", "amount": 52515.90, "incomeShare": 61.78 },
                { "group": "Discretionary", "amount": 872.49, "incomeShare": 1.03 },
                { "group": "Unknown", "amount": 0.00, "incomeShare": 0.00 } ],
    "categories": [], "advice": [], "reason": "NoRuleMatched" } ]
}
"""

// Can: no salary, so no share and no advice (K9-A of INC-02). The group split is illustrative.
private let can = """
{
  "period": { "kind": "LastMonth", "startDate": "2026-09-01", "endDate": "2026-09-30" },
  "currencies": [ {
    "currencyCode": "TRY", "totalSpent": 15037.99, "incomeTotal": 0.00, "incomeBase": null, "spentShare": null,
    "groups": [ { "group": "Essential", "amount": 12000.00, "incomeShare": null },
                { "group": "Discretionary", "amount": 3037.99, "incomeShare": null },
                { "group": "Unknown", "amount": 0.00, "incomeShare": null } ],
    "categories": [], "advice": [], "reason": "NoIncome" } ]
}
"""

// Zeynep: TRY 43,11 % and USD 2,81 %, two blocks that are never added together. Group splits are illustrative.
private let zeynep = """
{
  "period": { "kind": "LastMonth", "startDate": "2026-09-01", "endDate": "2026-09-30" },
  "currencies": [
    { "currencyCode": "TRY", "totalSpent": 10490.00, "incomeTotal": 73000.00, "incomeBase": 24333.33, "spentShare": 43.11,
      "groups": [ { "group": "Essential", "amount": 10490.00, "incomeShare": 43.11 },
                  { "group": "Discretionary", "amount": 0.00, "incomeShare": 0.00 },
                  { "group": "Unknown", "amount": 0.00, "incomeShare": 0.00 } ],
      "categories": [], "advice": [], "reason": "NoRuleMatched" },
    { "currencyCode": "USD", "totalSpent": 37.99, "incomeTotal": 4050.00, "incomeBase": 1350.00, "spentShare": 2.81,
      "groups": [ { "group": "Essential", "amount": 0.00, "incomeShare": 0.00 },
                  { "group": "Discretionary", "amount": 37.99, "incomeShare": 2.81 },
                  { "group": "Unknown", "amount": 0.00, "incomeShare": 0.00 } ],
      "categories": [], "advice": [], "reason": "NoRuleMatched" } ]
}
"""

private let noSpending = """
{ "period": { "kind": "LastMonth", "startDate": "2026-09-01", "endDate": "2026-09-30" }, "currencies": [] }
"""

private let unreachableMessage = "Sunucuya ulaşılamıyor; bağlantıyı kontrol edip tekrar deneyin."

private func loaded(_ state: ListState<CurrencyAdvice>) -> [CurrencyAdvice] {
    if case .loaded(let currencies) = state { return currencies }
    return []
}

private func decimal(_ text: String) -> Decimal { Decimal(string: text)! }

/// A made-up currency for the pure display rules; only the fields a rule reads matter.
private func currency(spentShare: String?, shares: [String?], advice: [AdviceItem] = [],
                      reason: NoAdviceReason? = .noRuleMatched) -> CurrencyAdvice {
    let groups = zip([SpendingGroup.essential, .discretionary, .unknown], shares).map { group, share in
        GroupSpending(group: group, amount: 100, incomeShare: share.map(decimal))
    }
    return CurrencyAdvice(currencyCode: "TRY", totalSpent: 300, incomeBase: spentShare == nil ? nil : 1000,
                          spentShare: spentShare.map(decimal), groups: groups, advice: advice, reason: reason)
}

private func adviceItem(category: String?, monthly: String, annual: String) -> AdviceItem {
    AdviceItem(rule: category == nil ? .discretionaryShare : .categoryAboveUsual, categoryCode: category?.uppercased(),
               categoryDisplayName: category, monthlySaving: decimal(monthly), annualSaving: decimal(annual), message: "Metin.")
}

@MainActor
struct IncomeShareViewModelTests {
    @Test func theAdviceIsAskedForLastMonthWithTheToken() async {
        let app = TestApp { _ in json(200, selin) }

        await IncomeShareViewModel(api: app.api).load()

        #expect(app.requests.map(\.method) == ["GET"])
        #expect(app.requests.map(\.path) == ["/api/analytics/spending-advice"])
        #expect(app.requests.map(\.query) == ["period=LastMonth"])
        #expect(app.requests.first?.authorization == "Bearer token-abc")
    }

    @Test func selinsMonthKeepsTheBackendsFiguresAndHerOneAdvice() async throws {
        let app = TestApp { _ in json(200, selin) }
        let model = IncomeShareViewModel(api: app.api)

        await model.load()

        let currencies = loaded(model.state)
        #expect(currencies.map(\.currencyCode) == ["TRY"])
        let tl = try #require(currencies.first)
        #expect(tl.spentShare == decimal("90.36"))
        #expect(tl.groups.map(\.group) == [.essential, .discretionary, .unknown])
        #expect(tl.groups.map(\.incomeShare) == [decimal("55.44"), decimal("28.92"), decimal("6.00")])
        #expect(tl.advice.map(\.categoryDisplayName) == ["Eğlence"])
        #expect(tl.advice.map(\.monthlySaving) == [decimal("236.18")])
        #expect(tl.advice.map(\.annualSaving) == [decimal("2834.20")])
        #expect(tl.reason == nil)
        #expect(model.month == "Eylül 2026")
        #expect(model.refreshError == nil)
    }

    @Test func muratHasAShareButNoAdvice() async throws {
        let app = TestApp { _ in json(200, murat) }
        let model = IncomeShareViewModel(api: app.api)

        await model.load()

        let tl = try #require(loaded(model.state).first)
        #expect(tl.spentShare == decimal("62.81"))
        #expect(tl.advice.isEmpty)
        #expect(tl.reason == .noRuleMatched)
    }

    @Test func canHasNoIncomeSoNoShare() async throws {
        let app = TestApp { _ in json(200, can) }
        let model = IncomeShareViewModel(api: app.api)

        await model.load()

        let tl = try #require(loaded(model.state).first)
        #expect(tl.incomeBase == nil)
        #expect(tl.spentShare == nil)
        #expect(tl.reason == .noIncome)
    }

    @Test func currenciesStayApart() async {
        let app = TestApp { _ in json(200, zeynep) }
        let model = IncomeShareViewModel(api: app.api)

        await model.load()

        let currencies = loaded(model.state)
        #expect(currencies.map(\.currencyCode) == ["TRY", "USD"])
        #expect(currencies.map(\.spentShare) == [decimal("43.11"), decimal("2.81")])
    }

    @Test func noSpendingLastMonthIsTheEmptyState() async {
        let app = TestApp { _ in json(200, noSpending) }
        let model = IncomeShareViewModel(api: app.api)

        await model.load()

        #expect(model.state == .empty)
        #expect(model.month == "Eylül 2026")
    }

    @Test func anUnknownGroupIsAnErrorAndNotAGuess() async {
        let app = TestApp { _ in json(200, selin.replacingOccurrences(of: "\"Unknown\"", with: "\"Luxury\"")) }
        let model = IncomeShareViewModel(api: app.api)

        await model.load()

        guard case .failed = model.state else {
            Issue.record("A group the app does not know must not be drawn as another one: \(model.state)")
            return
        }
    }

    @Test func aFirstLoadThatFailsShowsTheMessage() async {
        let app = TestApp { _ in problem(500, "internal_error", "Beklenmeyen bir hata oluştu.") }
        let model = IncomeShareViewModel(api: app.api)

        await model.load()

        #expect(model.state == .failed("Beklenmeyen bir hata oluştu."))
        #expect(model.month == nil)
    }

    @Test func aFailedRefreshKeepsTheCardAndSaysSo() async {
        let answers = Answers([{ json(200, selin) }, { throw URLError(.notConnectedToInternet) }])
        let app = TestApp { try answers.next($0) }
        let model = IncomeShareViewModel(api: app.api)

        await model.load()
        await model.load()

        #expect(loaded(model.state).map(\.spentShare) == [decimal("90.36")])
        #expect(model.month == "Eylül 2026")
        #expect(model.refreshError == unreachableMessage)
    }
}

// The card's words and bar, decided without a view.
@MainActor
struct IncomeShareDisplayTests {
    @Test(arguments: [("2026-09-01", "Eylül 2026"), ("2026-01-01", "Ocak 2026"), ("2025-12-01", "Aralık 2025")])
    func theMonthIsWrittenFromTheBackendsFirstDay(_ startDate: String, _ expected: String) {
        #expect(IncomeShareDisplay.month(startDate) == expected)
    }

    @Test func anUnreadableDayGivesNoMonth() {
        #expect(IncomeShareDisplay.month("2026-13-01") == nil)
        #expect(IncomeShareDisplay.month("Eylül") == nil)
    }

    @Test func theHeadlineWritesTheBackendsShareWithoutASuffix() {
        #expect(IncomeShareDisplay.headline(currency(spentShare: "90.36", shares: ["55.44", "28.92", "6.00"]))
                == "Gelirinizin %90,36 kadarı")
        #expect(IncomeShareDisplay.headline(currency(spentShare: "134.20", shares: ["80", "40", "14.20"]))
                == "Gelirinizin %134,20 kadarı")
    }

    @Test func withoutIncomeTheHeadlineSaysWhy() {
        #expect(IncomeShareDisplay.headline(currency(spentShare: nil, shares: [nil, nil, nil], reason: .noIncome))
                == "Maaş geliri görünmediği için gelir payı hesaplanamıyor.")
    }

    @Test func theSegmentsAreTheBackendsSharesInGroupOrder() {
        let segments = IncomeShareDisplay.segments(currency(spentShare: "90.36", shares: ["55.44", "28.92", "6.00"]))
        #expect(segments.map(\.group) == [.essential, .discretionary, .unknown])
        #expect(segments.map(\.fraction) == [0.5544, 0.2892, 0.06])
    }

    // K3-A: spending above income fills the bar and stops there; the text still says the whole figure.
    @Test func aShareAboveIncomeFillsTheBarAndIsCutAtTheEnd() {
        let over = currency(spentShare: "134.20", shares: ["80", "40", "14.20"])
        let segments = IncomeShareDisplay.segments(over)
        #expect(segments.map(\.fraction) == [0.8, 0.2, 0])
        #expect(IncomeShareDisplay.isAboveIncome(over))
        #expect(!IncomeShareDisplay.isAboveIncome(currency(spentShare: "90.36", shares: ["55.44", "28.92", "6.00"])))
        #expect(!IncomeShareDisplay.isAboveIncome(currency(spentShare: "100.00", shares: ["100", "0", "0"])))
    }

    @Test func withoutIncomeThereIsNoBar() {
        #expect(IncomeShareDisplay.segments(currency(spentShare: nil, shares: [nil, nil, nil], reason: .noIncome)).isEmpty)
    }

    @Test func theLegendNamesEachGroupWithItsShare() {
        let legend = IncomeShareDisplay.segments(currency(spentShare: "90.36", shares: ["55.44", "28.92", "6.00"])).map(\.label)
        #expect(legend == ["Temel %55,44", "İsteğe bağlı %28,92", "Bilinmiyor %6,00"])
    }

    @Test func anAdviceLineNamesTheCategoryAndTheTwoSavings() {
        let line = IncomeShareDisplay.adviceLine(adviceItem(category: "Eğlence", monthly: "236.18", annual: "2834.20"),
                                                 currency: "TRY")
        #expect(line == "Eğlence · ayda 236,18 TL, yılda 2.834,20 TL")
    }

    // The share rule names no category: it is about every discretionary category together.
    @Test func theShareRuleIsNamedAsTheWholeGroup() {
        let line = IncomeShareDisplay.adviceLine(adviceItem(category: nil, monthly: "100.00", annual: "1200.00"), currency: "TRY")
        #expect(line == "İsteğe bağlı harcamalar · ayda 100,00 TL, yılda 1.200,00 TL")
    }

    @Test func noAdviceSaysWhyInFixedWords() {
        #expect(IncomeShareDisplay.reasonText(.noRuleMatched) == "Öneri yok; harcamalarınız kuralların içinde kalıyor.")
        #expect(IncomeShareDisplay.reasonText(.noIncome) == nil)
    }

    // K2-B: three groups that are told apart, none of them the colour of money coming in or of an error.
    @Test func theGroupColoursAreToldApartAndReadOnTheCard() {
        let swatches = SpendingGroup.allCases.map(IncomeShareDisplay.swatch)
        for (index, first) in swatches.enumerated() {
            for second in swatches.dropFirst(index + 1) {
                #expect(first.distance(to: second) >= 25)
            }
            #expect(first.distance(to: Palette.incoming) >= 25)
            #expect(first.distance(to: Palette.failure) >= 25)
            #expect(first.contrast(with: Palette.surface) >= 3)
        }
    }

    // At most two pieces of advice per currency (INC-02 K9-A); a third from a changed backend is not shown.
    @Test func atMostTwoPiecesOfAdviceAreShown() {
        let three = (1...3).map { adviceItem(category: "Kategori \($0)", monthly: "1.00", annual: "12.00") }
        let shown = IncomeShareDisplay.shownAdvice(currency(spentShare: "50", shares: ["10", "30", "10"], advice: three, reason: nil))
        #expect(shown.map(\.categoryDisplayName) == ["Kategori 1", "Kategori 2"])
    }

    @Test func thePlaceholderCarriesOnlyNeutralFigures() {
        let placeholder = Skeleton.incomeShare
        #expect(placeholder.groups.count == SpendingGroup.allCases.count)
        #expect(placeholder.advice.isEmpty)
        #expect(placeholder.spentShare == 50)
    }
}
