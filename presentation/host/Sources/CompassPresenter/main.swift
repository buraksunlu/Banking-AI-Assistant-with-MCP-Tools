import AppKit
import ApplicationServices
import PresenterCore
import WebKit

/// A borderless window can only take the keyboard when it says so; the slides need ←/→, N, O and Enter.
final class StageWindow: NSWindow {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

/// After a tap on the simulator the stage is in the background; the first click on a slide's button should press it, not only
/// bring the stage forward (found in the K3 trial: the click never reached the page).
final class StageView: WKWebView {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

/// The stage (DEMO-05): one window over the main screen's visible area showing the slides from presentation/server.py, with the
/// simulator in the hole on the left. The window sits one level under normal windows (K3-A), so a click on a slide never covers
/// the simulator. The hole follows the simulator wherever it is, which needs no permission; with the Accessibility permission the
/// stage also moves and sizes the simulator itself (K4-D). Commands to the simulator still go through the server (K2-A): this
/// program only shows the page and places the phone.
@MainActor
final class Presenter: NSObject, NSApplicationDelegate, WKNavigationDelegate, WKScriptMessageHandler {
    private let url: URL
    private var window: StageWindow?
    private var webView: WKWebView?
    private var snapper: Snapper
    private let visible: Rect
    private let primaryHeight: Double
    private var shown: Hole??
    private var attempts = 0

    init(url: URL) {
        self.url = url
        // The stage takes the main screen (the one with the menu bar): its frame is where accessibility coordinates start.
        let screen = NSScreen.screens.first ?? NSScreen()
        visible = Rect(x: screen.visibleFrame.minX, y: screen.visibleFrame.minY,
                       width: screen.visibleFrame.width, height: screen.visibleFrame.height)
        primaryHeight = screen.frame.height
        snapper = Snapper(visible: visible, primaryHeight: primaryHeight)
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.mainMenu = Self.menu()
        let frame = NSRect(x: visible.x, y: visible.y, width: visible.width, height: visible.height)
        let window = StageWindow(contentRect: frame, styleMask: [.borderless], backing: .buffered, defer: false)
        window.level = NSWindow.Level(rawValue: NSWindow.Level.normal.rawValue - 1)
        window.backgroundColor = NSColor(red: 0x14 / 255, green: 0x10 / 255, blue: 0x0C / 255, alpha: 1)
        window.collectionBehavior = [.canJoinAllSpaces]

        let configuration = WKWebViewConfiguration()
        configuration.userContentController.add(self, name: "presenter")
        let webView = StageView(frame: frame, configuration: configuration)
        webView.navigationDelegate = self
        window.contentView = webView
        window.makeKeyAndOrderFront(nil)
        window.makeFirstResponder(webView)
        self.window = window
        self.webView = webView

        NSApp.activate()
        webView.load(URLRequest(url: url))
        Task { [weak self] in
            while let self {
                self.tick()
                try? await Task.sleep(for: .seconds(1))
            }
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    /// One look at the simulator: move or size it when allowed, then tell the page where it is. The permission is only checked,
    /// never asked for: asking on every start showed a system prompt each time the stage was reopened (DEMO-05 PR2 finding).
    private func tick() {
        guard let frame = SimulatorWindow.onScreen() else {
            snapper.lost()
            publish(nil)
            return
        }
        // The accessibility list sometimes misses the window for a tick; it is only needed to move it, never to draw the hole.
        if AXIsProcessTrusted(), let window = SimulatorWindow.find() {
            switch snapper.step(at: frame.position, size: frame.size) {
            case .resize(let size): window.resize(to: size)
            case .move(let point): window.move(to: point)
            case .stay: break
            }
        }
        publish(Stage.hole(at: frame.position, size: frame.size, in: visible, primaryHeight: primaryHeight))
    }

    /// Hands the hole to the page; nil means no simulator on the left, and the page draws its dashed frame.
    private func publish(_ hole: Hole?, force: Bool = false) {
        guard force || shown != .some(hole), let webView else { return }
        shown = .some(hole)
        let argument = hole.map {
            "{\"x\":\($0.phone.x),\"y\":\($0.phone.y),\"width\":\($0.phone.width),\"height\":\($0.phone.height),\"column\":\($0.column)}"
        } ?? "null"
        webView.evaluateJavaScript("window.compassStage && window.compassStage(\(argument))")
    }

    /// The page says "ready" once its script runs; it gets the current hole even if nothing changed since the last tick.
    func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage) {
        guard message.body as? String == "ready" else { return }
        if case .some(let hole) = shown { publish(hole, force: true) }
    }

    /// The server may still be starting; try again for ten seconds before saying so.
    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        attempts += 1
        guard attempts <= 20 else {
            webView.loadHTMLString(Self.unreachable(url), baseURL: nil)
            return
        }
        Task { [weak webView, url] in
            try? await Task.sleep(for: .milliseconds(500))
            webView?.load(URLRequest(url: url))
        }
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        attempts = 0
    }

    private static func unreachable(_ url: URL) -> String {
        """
        <body style="background:#14100C;color:#F7EFE2;font:20px -apple-system;display:grid;place-items:center;height:100vh;margin:0">
        <p>Sunum sayfası açılmadı: \(url.absoluteString) cevap vermiyor. <code>python3 presentation/server.py</code> çalışıyor mu?
        <br><small>⌘R yeniden dener · ⌘Q kapatır</small></p></body>
        """
    }

    @objc private func reload(_ sender: Any?) {
        attempts = 0
        webView?.load(URLRequest(url: url))
    }

    /// Quit and reload only: a borderless window has no title bar to close it from.
    private static func menu() -> NSMenu {
        let app = NSMenu()
        app.addItem(withTitle: "Yeniden yükle", action: #selector(reload(_:)), keyEquivalent: "r")
        app.addItem(.separator())
        app.addItem(withTitle: "Sunumu kapat", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        let item = NSMenuItem()
        item.submenu = app
        let bar = NSMenu()
        bar.addItem(item)
        return bar
    }
}

let address = CommandLine.arguments.dropFirst().first.flatMap(URL.init(string:)) ?? URL(string: "http://127.0.0.1:7300/")!
let application = NSApplication.shared
let presenter = Presenter(url: address)
application.delegate = presenter
application.setActivationPolicy(.regular)
application.run()
