import SwiftUI

/// The "Harcama analizi" screen (IOS-08): the category ring and, below it, the monthly bars.
struct AnalyticsView: View {
    let category: CategoryBreakdownViewModel
    let monthly: MonthlyTrendViewModel

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: Layout.large) {
                CategoryBreakdownSection(model: category)
                MonthlyTrendSection(model: monthly)
            }
            .padding(.horizontal, Layout.regular)
            .padding(.top, Layout.small)
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
            _ = await (categoryLoaded, monthlyLoaded)
        }
    }
}
