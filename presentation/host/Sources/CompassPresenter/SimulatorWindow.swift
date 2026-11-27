import AppKit
import ApplicationServices
import PresenterCore

/// The simulator's device window. Where it is comes from the window list, which needs no permission; moving and sizing it goes
/// through the accessibility API and needs the stage's Accessibility permission (K4-D).
@MainActor
struct SimulatorWindow {
    private static let bundleIdentifier = "com.apple.iphonesimulator"
    private let element: AXUIElement

    /// The largest on-screen window of the running simulator (the device; its menus and sheets are smaller), y down from the
    /// main screen's top; nil when the simulator is not running or shows no window.
    static func onScreen() -> (position: Point, size: Size)? {
        guard let app = NSRunningApplication.runningApplications(withBundleIdentifier: bundleIdentifier).first,
              let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID)
                as? [[String: Any]] else { return nil }
        let frames = list.compactMap { info -> CGRect? in
            guard info[kCGWindowOwnerPID as String] as? pid_t == app.processIdentifier,
                  info[kCGWindowLayer as String] as? Int == 0,
                  let bounds = info[kCGWindowBounds as String] as? [String: Any] else { return nil }
            return CGRect(dictionaryRepresentation: bounds as CFDictionary)
        }
        guard let device = frames.max(by: { $0.width * $0.height < $1.width * $1.height }) else { return nil }
        return (Point(x: device.minX, y: device.minY), Size(width: device.width, height: device.height))
    }

    /// The first device window of the running simulator; nil when the simulator is not running or shows no window.
    static func find() -> SimulatorWindow? {
        guard let app = NSRunningApplication.runningApplications(withBundleIdentifier: bundleIdentifier).first else { return nil }
        let application = AXUIElementCreateApplication(app.processIdentifier)
        guard let windows: [AXUIElement] = attribute(kAXWindowsAttribute, of: application) else { return nil }
        // The device window is a standard window; an open sheet or a menu is not and must not be moved into the hole.
        let device = windows.first { (attribute(kAXSubroleAttribute, of: $0) as String?) == kAXStandardWindowSubrole }
        return device.map(SimulatorWindow.init(element:))
    }

    /// The window's top-left corner (y down from the main screen's top) and size; nil if the window went away meanwhile.
    var frame: (position: Point, size: Size)? {
        guard let position: AXValue = Self.attribute(kAXPositionAttribute, of: element),
              let size: AXValue = Self.attribute(kAXSizeAttribute, of: element) else { return nil }
        var point = CGPoint.zero, extent = CGSize.zero
        guard AXValueGetValue(position, .cgPoint, &point), AXValueGetValue(size, .cgSize, &extent) else { return nil }
        return (Point(x: point.x, y: point.y), Size(width: extent.width, height: extent.height))
    }

    /// Moves the window; a failure is left to the next tick, which reads where the window really is.
    func move(to point: Point) {
        var value = CGPoint(x: point.x, y: point.y)
        guard let position = AXValueCreate(.cgPoint, &value) else { return }
        AXUIElementSetAttributeValue(element, kAXPositionAttribute as CFString, position)
    }

    /// Resizes the window; the simulator keeps the device's shape and may round the size.
    func resize(to size: Size) {
        var value = CGSize(width: size.width, height: size.height)
        guard let extent = AXValueCreate(.cgSize, &value) else { return }
        AXUIElementSetAttributeValue(element, kAXSizeAttribute as CFString, extent)
    }

    private static func attribute<T>(_ name: String, of element: AXUIElement) -> T? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, name as CFString, &value) == .success else { return nil }
        return value as? T
    }
}
