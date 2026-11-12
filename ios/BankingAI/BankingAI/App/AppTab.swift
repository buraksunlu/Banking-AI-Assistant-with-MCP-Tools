import Foundation

/// The signed-in app's four tabs, in the order the bar shows them (UI-07 K2-A).
enum AppTab: CaseIterable, Hashable {
    case home, analysis, assistant, profile

    var title: String {
        switch self {
        case .home: "Ana sayfa"
        case .analysis: "Analiz"
        // What the tab does, not the brand: the chat screen's own title is still `Brand.assistantName` (K3-A).
        case .assistant: "Asistan"
        case .profile: "Profil"
        }
    }

    /// An SF Symbol name; no sparkles or other stock "AI" marks (UI-02).
    var symbol: String {
        switch self {
        case .home: "house"
        case .analysis: "chart.pie"
        case .assistant: "bubble.left.and.text.bubble.right"
        case .profile: "person.crop.circle"
        }
    }
}
