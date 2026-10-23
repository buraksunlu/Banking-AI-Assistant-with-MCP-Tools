import Foundation

/// The period sent to the API; named periods are resolved by the business clock, never the phone's clock.
enum TransactionFilter: Equatable, Hashable {
    case allTime, thisMonth, lastMonth, last3Months, last6Months
    case custom(from: String, to: String)

    static let presets: [TransactionFilter] = [.allTime, .thisMonth, .lastMonth, .last3Months, .last6Months]

    /// The human-readable name of the applied or draft selection.
    var title: String {
        switch self {
        case .allTime: "Tüm geçmiş"
        case .thisMonth: "Bu ay"
        case .lastMonth: "Geçen ay"
        case .last3Months: "Son 3 ay"
        case .last6Months: "Son 6 ay"
        case .custom: "Özel tarih aralığı"
        }
    }

    /// Query fields only; the page size and cursor are added by the request builder.
    var query: [URLQueryItem] {
        let name: String
        switch self {
        case .allTime: name = "AllTime"
        case .thisMonth: name = "ThisMonth"
        case .lastMonth: name = "LastMonth"
        case .last3Months: name = "Last3Months"
        case .last6Months: name = "Last6Months"
        case .custom(let from, let to):
            // The API takes a named period or both days, never both; a range is days only, without a period name.
            return [URLQueryItem(name: "from", value: from), URLQueryItem(name: "to", value: to)]
        }
        return [URLQueryItem(name: "period", value: name)]
    }

    /// Local shape and range checks. The server alone decides whether a date is in the future.
    var validationMessage: String? {
        guard case .custom(let from, let to) = self else { return nil }
        guard let start = Self.date(from), let end = Self.date(to) else { return "Geçerli bir başlangıç ve bitiş tarihi seçin." }
        guard start <= end else { return "Başlangıç tarihi bitiş tarihinden sonra olamaz." }
        if let earliest = Self.calendar.date(byAdding: .month, value: -24, to: end), start < earliest {
            return "Dönem en fazla 24 ay olabilir."
        }
        return nil
    }

    /// A fixed Gregorian calendar used only as a carrier for date picker days, without time-zone conversion.
    static var calendar: Calendar {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(secondsFromGMT: 0)!
        return calendar
    }

    /// Strictly reads an API calendar day; invalid days cannot roll into the following month.
    static func date(_ day: String) -> Date? {
        let parts = day.split(separator: "-").compactMap { Int($0) }
        guard day.count == 10, parts.count == 3,
              let date = calendar.date(from: DateComponents(year: parts[0], month: parts[1], day: parts[2])),
              Self.day(date) == day else { return nil }
        return date
    }

    /// Writes the day picked in the same fixed calendar, never an ISO timestamp.
    static func day(_ date: Date) -> String {
        let parts = calendar.dateComponents([.year, .month, .day], from: date)
        return String(format: "%04d-%02d-%02d", parts.year!, parts.month!, parts.day!)
    }
}
