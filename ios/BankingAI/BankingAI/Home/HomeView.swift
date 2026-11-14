import SwiftUI

/// The home tab (UX-01 PR2, H2): the header and the "bu ay" panel — this month's pace, where the money went and last month
/// against income. Each card has its own REST answer and none needs the assistant, so the panel stays full when the model is
/// down. The accounts and transactions moved to the profile tab (K6-A); the cards' links switch to the analysis tab.
struct HomeView: View {
    let model: HomeViewModel
    let pace: MonthPaceViewModel
    let categories: CategoryBreakdownViewModel
    let income: IncomeShareViewModel
    /// Switches the tab bar to another tab.
    let show: (AppTab) -> Void

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: Layout.large) {
                    HomeHeader(model: model, dayLine: HomeDisplay.dayLine(pace.endDate)) { show(.profile) }
                    VStack(alignment: .leading, spacing: Layout.medium) {
                        // A failed card is not a button: its own "Tekrar dene" must not also switch to the analysis tab.
                        if case .failed = pace.state {
                            MonthPaceCard(model: pace)
                        } else {
                            Button { show(.analysis) } label: {
                                MonthPaceCard(model: pace)
                            }
                            .buttonStyle(.plain)
                            .accessibilityHint("Harcama analizini açar")
                        }
                        WhereItWentCard(model: categories) { show(.analysis) }
                        IncomeGlanceCard(model: income) { show(.analysis) }
                    }
                }
                .padding(.horizontal, Layout.regular)
                .padding(.top, Layout.small)
                .padding(.bottom, Layout.large)
            }
            .background(Palette.background.color)
            .refreshable {
                async let paceLoaded: Void = pace.load()
                async let categoriesLoaded: Void = categories.load()
                async let incomeLoaded: Void = income.load()
                _ = await (paceLoaded, categoriesLoaded, incomeLoaded)
            }
            .navigationTitle(AppTab.home.title)
            .toolbar(.hidden, for: .navigationBar)
            .task { await model.load() }
        }
    }
}
