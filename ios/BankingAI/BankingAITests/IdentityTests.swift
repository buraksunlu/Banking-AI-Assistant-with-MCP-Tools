import Foundation
import UIKit
import Testing
@testable import BankingAI

// UI-04: the identity lives in places a screenshot would not catch (Info.plist, the asset catalogue). Each rule that could
// drift without anything failing is pinned here.
struct IdentityTests {
    @Test func theHomeScreenNameIsTheBrandName() {
        #expect(Bundle.main.object(forInfoDictionaryKey: "CFBundleDisplayName") as? String == Brand.appName)
    }

    @Test func theLaunchScreenNamesItsBackgroundColour() throws {
        let launch = try #require(Bundle.main.object(forInfoDictionaryKey: "UILaunchScreen") as? [String: Any])
        #expect(launch["UIColorName"] as? String == "LaunchBackground")
    }

    // The launch colour must be the screen colour, or the app visibly flashes from one black to another when it opens.
    @Test(arguments: [UIUserInterfaceStyle.light, .dark])
    func theLaunchColourIsThePaletteBackground(style: UIUserInterfaceStyle) throws {
        let asset = try #require(UIColor(named: "LaunchBackground", in: Bundle.main, compatibleWith: UITraitCollection(userInterfaceStyle: style)))
        var (red, green, blue, alpha): (CGFloat, CGFloat, CGFloat, CGFloat) = (0, 0, 0, 0)
        asset.getRed(&red, green: &green, blue: &blue, alpha: &alpha)
        let hex = Palette.background.hex
        #expect(abs(red - CGFloat((hex >> 16) & 0xFF) / 255) < 0.003)
        #expect(abs(green - CGFloat((hex >> 8) & 0xFF) / 255) < 0.003)
        #expect(abs(blue - CGFloat(hex & 0xFF) / 255) < 0.003)
    }

    // Pins the label the chat shows above an answer. It does not guard the Turkish capitals: "kılavuz" has no dotted i, so
    // plain uppercased() gives the same text; that rule is guarded by DesignSystemTests.sectionLabelsUseTurkishCapitals.
    @Test func theAssistantLabelIsKILAVUZ() {
        #expect(Typography.label(Brand.assistantName) == "KILAVUZ")
    }

    @Test func theAssistantMarkLetterIsTheNamesFirstLetter() {
        #expect(Brand.assistantName.hasPrefix(Brand.assistantInitial))
        #expect(Brand.askAssistant == "\(Brand.assistantName)'a sor")
    }

    // UX-01 Z3: the accent is the dark amber, not the icon's blue any more; green stays the colour of money coming in, so the
    // two must never become the same.
    @Test func theAccentIsTheDarkAmberAndNotTheIncomingGreen() {
        #expect(Palette.accent == Swatch(0x9A5100))
        #expect(Palette.accent != Palette.incoming)
    }

    // The login error sits on the screen colour, not on a card.
    @Test func theFailureColourIsReadableOnTheBackground() {
        #expect(Palette.failure.contrast(with: Palette.background) >= 4.5)
    }
}
