import SwiftUI

/// The home tab (UX-01 PR2, H2): the header and the "bu ay" panel — this month's pace, where the money went and the payments
/// of the next 30 days (DEMO-03, in place of last month against income, which stays on the analysis tab). Each card has its own REST answer and none needs the assistant, so the panel stays full when the model is
/// down. The accounts and transactions moved to the profile tab (K6-A); the cards' links switch to the analysis tab.
struct HomeView: View {
    let model: HomeViewModel
    let pace: MonthPaceViewModel
    let categories: CategoryBreakdownViewModel
    let upcoming: UpcomingPaymentsViewModel
    /// Switches the tab bar to another tab.
    let show: (AppTab) -> Void
    /// Opens the assistant sheet: with a panel row's question, or empty from the "Kılavuz'a sor…" field (UX-01 PR5).
    let ask: (String?) -> Void

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
                        WhereItWentCard(model: categories, showAll: { show(.analysis) }, ask: { ask($0) })
                        UpcomingPaymentsCard(model: upcoming)
                    }
                }
                .padding(.horizontal, Layout.regular)
                .padding(.top, Layout.small)
                .padding(.bottom, Layout.large)
            }
            .background(Palette.background.color)
            // Above the tab bar, in reach of the thumb (K7-A); it opens the sheet, it is not a field of its own.
            .safeAreaInset(edge: .bottom) { askField }
            .refreshable {
                async let paceLoaded: Void = pace.load()
                async let categoriesLoaded: Void = categories.load()
                async let upcomingLoaded: Void = upcoming.load()
                _ = await (paceLoaded, categoriesLoaded, upcomingLoaded)
            }
            .navigationTitle(AppTab.home.title)
            .toolbar(.hidden, for: .navigationBar)
            .task { await model.load() }
        }
    }

    private var askField: some View {
        Button { ask(nil) } label: {
            HStack(spacing: Layout.small) {
                AssistantMark()
                Text("\(Brand.askAssistant)…")
                    .font(Typography.body)
                    .foregroundStyle(Palette.secondaryText.color)
                Spacer()
            }
            .padding(.horizontal, Layout.regular)
            .frame(minHeight: 48)
            .background(Palette.surface.color, in: RoundedRectangle(cornerRadius: Layout.cardRadius))
            .overlay(RoundedRectangle(cornerRadius: Layout.cardRadius).stroke(Palette.hairline.color, lineWidth: 1))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .padding(.horizontal, Layout.regular)
        .padding(.vertical, Layout.small)
        .background(Palette.background.color)
    }
}
