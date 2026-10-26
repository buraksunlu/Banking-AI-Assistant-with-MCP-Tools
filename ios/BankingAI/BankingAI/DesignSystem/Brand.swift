import Foundation

/// The names the user sees, in one place (UI-04 K1, K2): the app, and the assistant inside it. The app's display name also
/// lives in Info.plist (what the home screen writes under the icon); a test keeps the two equal.
nonisolated enum Brand {
    static let appName = "Compass"
    static let tagline = "Harcamalarınızda yön bulun"

    static let assistantName = "Kılavuz"
    /// The letter in the assistant's ring (``AssistantMark``); the icon carries the app's own letter, so the two never mix.
    static let assistantInitial = "K"
    /// The Turkish dative of the name: "Kılavuz'a sor". A proper noun takes its suffix after an apostrophe.
    static let askAssistant = "Kılavuz'a sor"
}
