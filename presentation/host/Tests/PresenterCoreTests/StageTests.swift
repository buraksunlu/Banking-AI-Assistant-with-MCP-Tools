import Testing
@testable import PresenterCore

/// The stage's arithmetic (DEMO-05 K4-D). Figures worked out by hand: the phone fills the visible height less the top margin (24)
/// and the band under it for the tab buttons and the status line (88), keeps the simulator's shape and rounds its width.
struct StageTests {
    private let laptop = Rect(x: 0, y: 0, width: 1440, height: 900)

    @Test func fitsThePhoneToTheVisibleHeight() {
        // 900 − 24 − 88 = 788 high; 788 × 400 / 800 = 394 wide.
        #expect(Stage.fit(Size(width: 400, height: 800), in: laptop) == Size(width: 394, height: 788))
    }

    @Test func roundsAHalfPointWidthUp() {
        // 875 − 112 = 763 high; 763 × 0.5 = 381.5 → 382.
        let visible = Rect(x: 0, y: 0, width: 1440, height: 875)
        #expect(Stage.fit(Size(width: 400, height: 800), in: visible) == Size(width: 382, height: 763))
    }

    @Test func putsTheHomeUnderTheTopMargin() {
        #expect(Stage.home(in: laptop, primaryHeight: 900) == Point(x: 24, y: 24))
    }

    @Test func countsTheHomeFromTheTopOfTheMainScreen() {
        // AppKit counts y up from the bottom, window lists and the accessibility API down from the top: a 25 point menu bar
        // above a visible area of 875 puts the phone at 25 + 24 = 49. Without the flip it would land at 24, under the menu bar.
        #expect(Stage.home(in: Rect(x: 0, y: 0, width: 1440, height: 875), primaryHeight: 900) == Point(x: 24, y: 49))
    }

    @Test func followsTheVisibleAreaWhenTheDockIsOnTheLeft() {
        #expect(Stage.home(in: Rect(x: 80, y: 0, width: 1360, height: 900), primaryHeight: 900) == Point(x: 104, y: 24))
    }
}

/// The hole the page draws around the simulator where it really is (K4-D): it needs no permission, so it works whether or not
/// the stage may move the window.
struct HoleTests {
    private let laptop = Rect(x: 0, y: 0, width: 1440, height: 900)

    @Test func drawsTheHoleUnderASimulatorAtHome() {
        // Menu bar 25: the screen's (24, 49) is the stage window's (24, 24); the column ends a margin after the phone.
        let visible = Rect(x: 0, y: 0, width: 1440, height: 875)
        let hole = Stage.hole(at: Point(x: 24, y: 49), size: Size(width: 382, height: 763), in: visible, primaryHeight: 900)
        #expect(hole == Hole(phone: Rect(x: 24, y: 24, width: 382, height: 763), column: 430))
    }

    @Test func followsASimulatorPutThereByHand() {
        // Dragged to (60, 100): 60 + 394 + 24 = 478.
        let hole = Stage.hole(at: Point(x: 60, y: 100), size: Size(width: 394, height: 788), in: laptop, primaryHeight: 900)
        #expect(hole == Hole(phone: Rect(x: 60, y: 100, width: 394, height: 788), column: 478))
    }

    @Test func countsFromTheVisibleAreaWhenTheDockIsOnTheLeft() {
        let visible = Rect(x: 80, y: 0, width: 1360, height: 900)
        let hole = Stage.hole(at: Point(x: 104, y: 24), size: Size(width: 394, height: 788), in: visible, primaryHeight: 900)
        #expect(hole == Hole(phone: Rect(x: 24, y: 24, width: 394, height: 788), column: 442))
    }

    @Test func drawsNoHoleForASimulatorOnTheRight() {
        // 900 + 394 = 1294 ends past half the stage (720): the slides would sit under it.
        #expect(Stage.hole(at: Point(x: 900, y: 24), size: Size(width: 394, height: 788), in: laptop, primaryHeight: 900) == nil)
    }

    @Test func drawsNoHoleForASimulatorAboveTheStage() {
        #expect(Stage.hole(at: Point(x: 24, y: 10), size: Size(width: 394, height: 788), in: Rect(x: 0, y: 0, width: 1440, height: 875),
                           primaryHeight: 900) == nil)
    }
}

/// What the stage does with the simulator window once a second when it may (K4-D): resize it once when it is new or resized by
/// hand, then keep it at home. The simulator may refuse or round a size; the stage settles on what it got instead of asking again.
struct SnapperTests {
    private let laptop = Rect(x: 0, y: 0, width: 1440, height: 900)

    @Test func resizesANewWindowOnce() {
        var snapper = Snapper(visible: laptop, primaryHeight: 900)
        #expect(snapper.step(at: Point(x: 300, y: 300), size: Size(width: 400, height: 800)) == .resize(Size(width: 394, height: 788)))
    }

    @Test func movesTheFittedWindowHome() {
        var snapper = Snapper(visible: laptop, primaryHeight: 900)
        _ = snapper.step(at: Point(x: 300, y: 300), size: Size(width: 400, height: 800))
        #expect(snapper.step(at: Point(x: 300, y: 300), size: Size(width: 394, height: 788)) == .move(Point(x: 24, y: 24)))
    }

    @Test func leavesAWindowAtHomeAlone() {
        var snapper = Snapper(visible: laptop, primaryHeight: 900)
        _ = snapper.step(at: Point(x: 300, y: 300), size: Size(width: 400, height: 800))
        _ = snapper.step(at: Point(x: 300, y: 300), size: Size(width: 394, height: 788))
        #expect(snapper.step(at: Point(x: 24.4, y: 24), size: Size(width: 394, height: 788)) == .stay)
    }

    @Test func settlesOnTheSizeTheSimulatorKept() {
        // Asked for 394 × 788, the simulator stayed 400 × 800 (as Xcode 26's does): no second resize, only the move.
        var snapper = Snapper(visible: laptop, primaryHeight: 900)
        _ = snapper.step(at: Point(x: 300, y: 300), size: Size(width: 400, height: 800))
        #expect(snapper.step(at: Point(x: 300, y: 300), size: Size(width: 400, height: 800)) == .move(Point(x: 24, y: 24)))
        #expect(snapper.step(at: Point(x: 24, y: 24), size: Size(width: 400, height: 800)) == .stay)
    }

    @Test func resizesAgainWhenResizedByHand() {
        var snapper = Snapper(visible: laptop, primaryHeight: 900)
        _ = snapper.step(at: Point(x: 300, y: 300), size: Size(width: 400, height: 800))
        _ = snapper.step(at: Point(x: 24, y: 24), size: Size(width: 394, height: 788))
        #expect(snapper.step(at: Point(x: 24, y: 24), size: Size(width: 300, height: 600)) == .resize(Size(width: 394, height: 788)))
    }

    @Test func startsOverWhenTheWindowIsGone() {
        // The window closed while the stage waited to see its resize; the next window is a new one and gets its own resize.
        var snapper = Snapper(visible: laptop, primaryHeight: 900)
        _ = snapper.step(at: Point(x: 300, y: 300), size: Size(width: 400, height: 800))
        snapper.lost()
        #expect(snapper.step(at: Point(x: 300, y: 300), size: Size(width: 400, height: 800)) == .resize(Size(width: 394, height: 788)))
    }
}
