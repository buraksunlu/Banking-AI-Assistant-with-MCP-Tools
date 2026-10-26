import SwiftUI

/// The top line of the home screen: the greeting, today's date as the backend counts it, and the profile circle whose menu
/// signs out (UI-03 K1-A; UI-07 turns the menu into a profile screen).
struct HomeHeader: View {
    let model: HomeViewModel
    /// "26 Ekim 2026"; nil until the backend's day has arrived, then the line simply appears.
    let dayLine: String?

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
                            .redacted(reason: .placeholder)
                            // A redacted placeholder is still read aloud; a made-up name must not be.
                            .accessibilityHidden(true)
                    }
                    Text(dayLine ?? " ")
                        .font(Typography.caption)
                        .foregroundStyle(Palette.secondaryText.color)
                        .accessibilityHidden(dayLine == nil)
                }
                Spacer(minLength: Layout.small)
                profileMenu
            }
            if let message = model.errorMessage {
                StatusBand(text: message, symbol: "exclamationmark.triangle", actionTitle: "Tekrar dene") {
                    Task { await model.load() }
                }
            }
        }
    }

    private var profileMenu: some View {
        Menu {
            Button("Çıkış", role: .destructive, action: model.signOut)
        } label: {
            Text(model.initials ?? "")
                .font(Typography.caption.weight(.semibold))
                .foregroundStyle(Palette.accent.color)
                .frame(width: 40, height: 40)
                .overlay(Circle().stroke(Palette.accent.color, lineWidth: 1))
        }
        .accessibilityLabel("Profil menüsü")
    }
}
