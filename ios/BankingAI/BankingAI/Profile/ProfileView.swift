import SwiftUI

/// The profile tab (UI-07 K6-B): who is signed in, read from the home header's `GET /api/me` answer, the app's version, and
/// signing out behind a confirmation (K7-B).
struct ProfileView: View {
    let model: HomeViewModel
    @State private var confirmingSignOut = false

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: Layout.large) {
                if let profile = model.profile {
                    details(profile)
                } else if let message = model.errorMessage {
                    StateMessage(title: "Profil yüklenemedi", symbol: "person.crop.circle.badge.exclamationmark",
                                 message: message, actionTitle: "Tekrar dene") {
                        Task { await model.load() }
                    }
                    .frame(maxWidth: .infinity)
                    .cardSurface()
                } else {
                    details(Skeleton.profile).skeleton()
                }
                if let version = ProfileDisplay.version(Bundle.main.infoDictionary ?? [:]) {
                    VStack(alignment: .leading, spacing: 0) {
                        DetailRow(label: "Uygulama sürümü", value: version)
                    }
                    // Full width at every text size: stacked rows at the largest sizes have no spacer to stretch the card.
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .cardSurface(padding: Layout.regular)
                }
                Button(role: .destructive) { confirmingSignOut = true } label: {
                    // On the text itself: the button style paints its label white and would win over a colour set outside.
                    Text("Çıkış").foregroundStyle(Palette.failure.color)
                }
                .buttonStyle(SecondaryButtonStyle())
                // On the button, not the screen: iOS 26 shows the dialog as a bubble pointing at what it is attached to.
                .confirmationDialog("Çıkış yapılsın mı?", isPresented: $confirmingSignOut, titleVisibility: .visible) {
                    Button("Çıkış", role: .destructive, action: model.signOut)
                    Button("Vazgeç", role: .cancel) {}
                } message: {
                    // Signing out cannot be undone: the chat lives only in memory (AI-08 K4-D).
                    Text("Sohbet geçmişi silinir.")
                }
                .frame(maxWidth: .infinity)
            }
            .padding(.horizontal, Layout.regular)
            .padding(.top, Layout.small)
            .padding(.bottom, Layout.large)
        }
        .blackScreen()
        .navigationTitle(AppTab.profile.title)
        .navigationBarTitleDisplayMode(.inline)
    }

    private func details(_ profile: MeResponse) -> some View {
        VStack(alignment: .leading, spacing: Layout.large) {
            VStack(alignment: .leading, spacing: Layout.tight) {
                Text(ProfileDisplay.fullName(first: profile.firstName, last: profile.lastName))
                    .font(Typography.title)
                    .foregroundStyle(Palette.primaryText.color)
                    .accessibilityAddTraits(.isHeader)
                Text(profile.username)
                    .font(Typography.caption)
                    .foregroundStyle(Palette.secondaryText.color)
            }
            VStack(alignment: .leading, spacing: 0) {
                DetailRow(label: "Kullanıcı adı", value: profile.username)
                HairlineDivider()
                DetailRow(label: "Müşteri numarası", value: profile.customerNumber)
                HairlineDivider()
                DetailRow(label: "Para birimi", value: profile.preferredCurrency)
                HairlineDivider()
                DetailRow(label: "Saat dilimi", value: ProfileDisplay.timeZone(profile.timeZone, at: Date()))
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .cardSurface(padding: Layout.regular)
        }
    }
}
