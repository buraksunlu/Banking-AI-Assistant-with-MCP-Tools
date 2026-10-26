import SwiftUI
import Testing
@testable import BankingAI

// UI-06 PR2: every animation of the app asks `Motion` first, so "Hareketi azalt" switches all of them off in one place.
struct MotionTests {
    @Test func reducedMotionMeansNoAnimation() {
        #expect(Motion.animation(.easeOut, reduceMotion: true) == nil)
        #expect(Motion.animation(Motion.standard, reduceMotion: true) == nil)
    }

    @Test func otherwiseTheAnimationIsTheOneAskedFor() {
        #expect(Motion.animation(.easeOut, reduceMotion: false) == .easeOut)
        #expect(Motion.animation(Motion.standard, reduceMotion: false) == Motion.standard)
    }

    // Short enough not to hold up a figure, long enough to be seen.
    @Test func theStandardMovementIsAQuarterSecond() {
        #expect(Motion.standard == .easeInOut(duration: 0.25))
    }
}
