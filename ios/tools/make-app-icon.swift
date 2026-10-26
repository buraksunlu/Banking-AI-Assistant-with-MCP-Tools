// Draws the app icon (UI-04 K6-A, K7-A): a thin gold ring around the app's first letter on the screen colour.
// The colours are read from Palette.swift, so the icon cannot drift from the app; run it again after a palette change:
//   swift ios/tools/make-app-icon.swift
import CoreGraphics
import CoreText
import Foundation
import ImageIO

let root = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
let palette = try String(contentsOf: root.appendingPathComponent("BankingAI/BankingAI/DesignSystem/Palette.swift"), encoding: .utf8)
let brand = try String(contentsOf: root.appendingPathComponent("BankingAI/BankingAI/DesignSystem/Brand.swift"), encoding: .utf8)

func hex(_ name: String) -> UInt32 {
    let pattern = try! NSRegularExpression(pattern: "static let \(name) = Swatch\\(0x([0-9A-Fa-f]{6})\\)")
    guard let match = pattern.firstMatch(in: palette, range: NSRange(palette.startIndex..., in: palette)),
          let range = Range(match.range(at: 1), in: palette), let value = UInt32(palette[range], radix: 16)
    else { fatalError("Palette.\(name) not found") }
    return value
}

func letter() -> String {
    let pattern = try! NSRegularExpression(pattern: "static let appName = \"(.)")
    guard let match = pattern.firstMatch(in: brand, range: NSRange(brand.startIndex..., in: brand)),
          let range = Range(match.range(at: 1), in: brand)
    else { fatalError("Brand.appName not found") }
    return String(brand[range])
}

func color(_ hex: UInt32) -> CGColor {
    CGColor(srgbRed: CGFloat((hex >> 16) & 0xFF) / 255, green: CGFloat((hex >> 8) & 0xFF) / 255, blue: CGFloat(hex & 0xFF) / 255, alpha: 1)
}

/// One icon. No alpha channel: the App Store refuses an icon with one.
func render(background: CGColor, mark: CGColor, to file: String) throws {
    let size = 1024
    let context = CGContext(data: nil, width: size, height: size, bitsPerComponent: 8, bytesPerRow: 0,
                            space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue)!
    context.setFillColor(background)
    context.fill(CGRect(x: 0, y: 0, width: size, height: size))

    context.setStrokeColor(mark)
    context.setLineWidth(14)
    context.strokeEllipse(in: CGRect(x: 192, y: 192, width: 640, height: 640))

    // The system font at a light weight, like the wordmark on the login screen.
    let base = CTFontCreateUIFontForLanguage(.system, 360, nil)!
    let descriptor = CTFontDescriptorCreateCopyWithAttributes(CTFontCopyFontDescriptor(base),
                                                              [kCTFontTraitsAttribute: [kCTFontWeightTrait: -0.4]] as CFDictionary)
    let light = CTFontCreateWithFontDescriptor(descriptor, 360, nil)
    let attributes: [CFString: Any] = [kCTFontAttributeName: light, kCTForegroundColorAttributeName: mark]
    let line = CTLineCreateWithAttributedString(CFAttributedStringCreate(nil, letter() as CFString, attributes as CFDictionary))
    // Centre on the letter's real outline, not on the line box, so a capital sits in the middle of the ring.
    let bounds = CTLineGetBoundsWithOptions(line, .useGlyphPathBounds)
    context.textPosition = CGPoint(x: (CGFloat(size) - bounds.width) / 2 - bounds.minX, y: (CGFloat(size) - bounds.height) / 2 - bounds.minY)
    CTLineDraw(line, context)

    let image = context.makeImage()!
    let url = root.appendingPathComponent("BankingAI/BankingAI/Assets.xcassets/AppIcon.appiconset/\(file)")
    let destination = CGImageDestinationCreateWithURL(url as CFURL, "public.png" as CFString, 1, nil)!
    CGImageDestinationAddImage(destination, image, nil)
    guard CGImageDestinationFinalize(destination) else { fatalError("could not write \(file)") }
}

let background = color(hex("background"))
let gold = color(hex("accent"))
try render(background: background, mark: gold, to: "icon-default.png")
try render(background: background, mark: gold, to: "icon-dark.png")
// The system tints this one itself; it only needs the shape in a light grey on black.
try render(background: color(0x000000), mark: color(hex("primaryText")), to: "icon-tinted.png")
print("icons written")
