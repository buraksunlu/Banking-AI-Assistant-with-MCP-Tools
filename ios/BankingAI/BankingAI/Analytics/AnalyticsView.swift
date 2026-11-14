import SwiftUI

/// The "Harcama analizi" screen (IOS-08): the category ring, the monthly bars and, since UX-01 PR2 (K3-D), the whole income
/// card with its advice, which the home panel's "Nerede kısarım?" leads to.
struct AnalyticsView: View {
    let category: CategoryBreakdownViewModel
    let monthly: MonthlyTrendViewModel
    let income: IncomeShareViewModel

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: Layout.large) {
                CategoryBreakdownSection(model: category)
                MonthlyTrendSection(model: monthly)
                IncomeShareCard(model: income)
            }
            .padding(.horizontal, Layout.regular)
            .padding(.top, Layout.small)
            .padding(.bottom, Layout.large)
        }
        .blackScreen()
        .navigationTitle("Harcama analizi")
        .navigationBarTitleDisplayMode(.inline)
        .task {
            async let categoryLoaded: Void = category.load()
            async let monthlyLoaded: Void = monthly.load()
            _ = await (categoryLoaded, monthlyLoaded)
        }
        .refreshable {
            async let categoryLoaded: Void = category.load()
            async let monthlyLoaded: Void = monthly.load()
            async let incomeLoaded: Void = income.load()
            _ = await (categoryLoaded, monthlyLoaded, incomeLoaded)
        }
    }
}
