import SwiftUI

/// Three dots lit in turn while the assistant answers (UI-02 K4-A); still when Reduce Motion is on.
struct TypingDots: View {
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    private static let step = 0.4

    var body: some View {
        TimelineView(.animation(minimumInterval: Self.step, paused: reduceMotion)) { context in
            let lit = Int(context.date.timeIntervalSinceReferenceDate / Self.step) % 3
            HStack(spacing: 5) {
                ForEach(0..<3, id: \.self) { index in
                    Circle()
                        .fill(Palette.secondaryText.color)
                        .frame(width: 6, height: 6)
                        .opacity(reduceMotion || index == lit ? 1 : 0.35)
                }
            }
            .animation(reduceMotion ? nil : .easeInOut(duration: 0.3), value: lit)
        }
        .accessibilityElement()
        .accessibilityLabel("Asistan yazıyor")
    }
}
