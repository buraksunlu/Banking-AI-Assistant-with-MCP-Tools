import Foundation

/// The profile screen's lines, decided without a view (UI-07).
enum ProfileDisplay {
    /// "Ada", "Kahve" → "Ada Kahve".
    static func fullName(first: String, last: String) -> String {
        [first, last]
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .filter { !$0.isEmpty }
            .joined(separator: " ")
    }

    /// "1.0 (1)" from the bundle's Info dictionary; nil when either part is missing, so the row hides instead of showing a
    /// made-up version.
    static func version(_ info: [String: Any]) -> String? {
        guard let marketing = info["CFBundleShortVersionString"] as? String,
              let build = info["CFBundleVersion"] as? String else { return nil }
        return "\(marketing) (\(build))"
    }

    /// "Europe/Istanbul · GMT+03:00" (K9-C): the exact name plus the offset on `date`, which moves with summer time.
    /// An identifier the system does not know is shown as it came, without an offset it could not work out.
    static func timeZone(_ identifier: String, at date: Date) -> String {
        guard let zone = TimeZone(identifier: identifier) else { return identifier }
        let seconds = zone.secondsFromGMT(for: date)
        let sign = seconds < 0 ? "-" : "+"
        let minutes = abs(seconds) / 60
        return "\(identifier) · GMT\(sign)" + String(format: "%02d:%02d", minutes / 60, minutes % 60)
    }
}
