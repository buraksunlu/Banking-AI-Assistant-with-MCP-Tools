import SwiftUI

/// The "Harcama analizi" screen (IOS-08): the category ring now, the monthly chart next to it in the following step.
struct AnalyticsView: View {
    let category: CategoryBreakdownViewModel

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 24) {
                CategoryBreakdownSection(model: category)
            }
            .padding()
        }
        .background(Color(.systemGroupedBackground))
        .navigationTitle("Harcama analizi")
        .navigationBarTitleDisplayMode(.inline)
        .task { await category.load() }
        .refreshable { await category.load() }
    }
}
