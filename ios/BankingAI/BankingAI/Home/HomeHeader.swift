import SwiftUI

/// The top line of the home screen: the greeting, today's date as the backend counts it, and the profile circle, which
/// switches to the profile tab (UI-03 K1-A; UI-07 K8-B: signing out moved there).
struct HomeHeader: View {
    let model: HomeViewModel
    /// "26 Ekim 2026"; nil until the backend's day has arrived, then the line simply appears.
    let dayLine: String?
    let showProfile: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: Layout.small) {
            HStack(alignment: .center, spacing: Layout.medium) {
                VStack(alignment: .leading, spacing: Layout.tight) {
                    if let greeting = model.greeting {
                        Text(greeting)
                            .font(Typography.title)
                            .foregroundStyle(Palette.primaryText.color)
                    } else {
                        // Keeps the line's height while it loads, so nothing jumps when the name arrives.
                        Text("Merhaba, Ada!")
                            .font(Typography.title)
                            // A made-up name must never be read aloud; the cards below already say they are loading.
                            .skeleton(spoken: false)
                    }
                    Text(dayLine ?? " ")
                        .font(Typography.caption)
                        .foregroundStyle(Palette.secondaryText.color)
                        .accessibilityHidden(dayLine == nil)
                }
                Spacer(minLength: Layout.small)
                profileButton
            }
            if let message = model.errorMessage {
                StatusBand(text: message, symbol: "exclamationmark.triangle", actionTitle: "Tekrar dene") {
                    Task { await model.load() }
                }
            }
        }
    }

    private var profileButton: some View {
        Button(action: showProfile) {
            Text(model.initials ?? "")
                .font(Typography.caption.weight(.semibold))
                // Two letters in a fixed ring: grown with the text they became "…" at the largest sizes. Held to the
                // large-content viewer instead (a long press shows them big).
                .dynamicTypeSize(...DynamicTypeSize.xxLarge)
                .foregroundStyle(Palette.accent.color)
                .frame(width: 44, height: 44)
                .overlay(Circle().stroke(Palette.accent.color, lineWidth: 1))
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Profil")
        .accessibilityShowsLargeContentViewer {
            Text(model.initials ?? "")
        }
    }
}
