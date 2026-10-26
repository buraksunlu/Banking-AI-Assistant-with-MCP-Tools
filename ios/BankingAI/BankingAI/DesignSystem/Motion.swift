import SwiftUI

/// The one rule for movement (UI-06 K6-C): every animation of the app is asked for here, and with "Hareketi azalt" on there is
/// none at all; changes then simply happen. Screen pushes stay the system's own.
enum Motion {
    /// A state change: placeholder to content, a new message, a figure turning over.
    static let standard = Animation.easeInOut(duration: 0.25)

    /// The animation to use, or nil (no animation) when the person has asked for less motion.
    static func animation(_ animation: Animation, reduceMotion: Bool) -> Animation? {
        reduceMotion ? nil : animation
    }
}

extension View {
    /// Animates whatever changes with `value` (the screen's state), unless "Hareketi azalt" is on. A view that comes or goes
    /// with it fades; a figure marked `.contentTransition(.numericText())` turns over.
    func motion<Value: Equatable>(_ animation: Animation = Motion.standard, value: Value) -> some View {
        modifier(MotionModifier(animation: animation, value: value))
    }
}

private struct MotionModifier<Value: Equatable>: ViewModifier {
    let animation: Animation
    let value: Value
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    func body(content: Content) -> some View {
        content.animation(Motion.animation(animation, reduceMotion: reduceMotion), value: value)
    }
}
