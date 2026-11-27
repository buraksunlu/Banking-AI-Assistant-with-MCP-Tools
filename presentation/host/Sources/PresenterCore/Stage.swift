/// A size in points.
public struct Size: Equatable, Sendable, CustomStringConvertible {
    public var width: Double
    public var height: Double

    public init(width: Double, height: Double) {
        self.width = width
        self.height = height
    }

    public var description: String { "\(width)×\(height)" }
}

/// A point in points.
public struct Point: Equatable, Sendable {
    public var x: Double
    public var y: Double

    public init(x: Double, y: Double) {
        self.x = x
        self.y = y
    }
}

/// A rectangle in points.
public struct Rect: Equatable, Sendable {
    public var x: Double
    public var y: Double
    public var width: Double
    public var height: Double

    public init(x: Double, y: Double, width: Double, height: Double) {
        self.x = x
        self.y = y
        self.width = width
        self.height = height
    }
}

/// What the page draws around the simulator: the phone in the stage window's own coordinates (y down from its top, CSS pixels)
/// and the width of the left column that holds it.
public struct Hole: Equatable, Sendable {
    public let phone: Rect
    public let column: Double

    public init(phone: Rect, column: Double) {
        self.phone = phone
        self.column = column
    }
}

/// The stage's arithmetic (DEMO-05 K4-D). The stage window covers the main screen's visible area; the phone's home is a margin in
/// from its top-left corner, with a band below it for the tab buttons and the status line. Screen points (`Point`) count y down
/// from the main screen's top, as window lists and the accessibility API do; `Rect`s of the visible area are AppKit's, y up.
public enum Stage {
    /// The space around the phone.
    public static let margin = 24.0
    /// The space under the phone for the tab buttons and the status line.
    public static let band = 88.0

    /// The size to give a simulator window of `simulator`'s shape so that it fills the visible height.
    public static func fit(_ simulator: Size, in visible: Rect) -> Size {
        let height = (visible.height - margin - band).rounded(.down)
        return Size(width: (height * simulator.width / simulator.height).rounded(), height: height)
    }

    /// Where the simulator window's top-left corner belongs on a stage covering `visible` (the main screen is `primaryHeight` high).
    public static func home(in visible: Rect, primaryHeight: Double) -> Point {
        Point(x: visible.x + margin, y: top(of: visible, primaryHeight: primaryHeight) + margin)
    }

    /// The hole for a simulator window where it really is; nil when it is not in the left half of the stage, where the slides
    /// would sit under it, and the page shows its dashed frame instead.
    public static func hole(at position: Point, size: Size, in visible: Rect, primaryHeight: Double) -> Hole? {
        let x = position.x - visible.x, y = position.y - top(of: visible, primaryHeight: primaryHeight)
        guard x >= 0, y >= 0, x + size.width <= visible.width / 2 else { return nil }
        return Hole(phone: Rect(x: x, y: y, width: size.width, height: size.height), column: x + size.width + margin)
    }

    /// The visible area's top edge, counted down from the main screen's top.
    private static func top(of visible: Rect, primaryHeight: Double) -> Double {
        primaryHeight - (visible.y + visible.height)
    }
}

/// What to do with the simulator window on one tick, when the stage may move it.
public enum SnapStep: Equatable, Sendable {
    /// Give the window this size; the next tick reads what the simulator made of it.
    case resize(Size)
    /// Move the window's top-left corner here.
    case move(Point)
    /// The window is where it belongs.
    case stay
}

/// Keeps the simulator at home when the stage has the Accessibility permission (K4-D): resizes a window once when it is new or was
/// resized by hand, then only moves it. The simulator may refuse or round a size, so the stage settles on what it got instead of
/// asking again every second.
public struct Snapper: Sendable {
    private let visible: Rect
    private let primaryHeight: Double
    /// The size the window had when the stage last accepted it; nil before the first window and after one is lost.
    private var settled: Size?
    /// True while waiting to see what the simulator made of a resize.
    private var resized = false

    public init(visible: Rect, primaryHeight: Double) {
        self.visible = visible
        self.primaryHeight = primaryHeight
    }

    /// One tick with the window's current top-left corner and size.
    public mutating func step(at position: Point, size: Size) -> SnapStep {
        if size != settled {
            let target = Stage.fit(size, in: visible)
            if target != size && !resized {
                resized = true
                return .resize(target)
            }
            settled = size
            resized = false
        }
        let home = Stage.home(in: visible, primaryHeight: primaryHeight)
        // Under a point off is the simulator's own rounding, not a window out of place.
        return abs(position.x - home.x) >= 1 || abs(position.y - home.y) >= 1 ? .move(home) : .stay
    }

    /// The window is gone (the simulator quit or was closed); the next one starts over.
    public mutating func lost() {
        settled = nil
        resized = false
    }
}
