// swift-tools-version: 6.0
// The presentation's window (DEMO-05): one full-screen stage with the simulator in a hole on the left and the slides on the right.
// Built with `swift build` (no Xcode project); started by scripts/present.sh.
import PackageDescription

let package = Package(
    name: "CompassPresenter",
    platforms: [.macOS(.v14)],
    targets: [
        .target(name: "PresenterCore"),
        .executableTarget(name: "CompassPresenter", dependencies: ["PresenterCore"]),
        .testTarget(name: "PresenterCoreTests", dependencies: ["PresenterCore"]),
    ]
)
