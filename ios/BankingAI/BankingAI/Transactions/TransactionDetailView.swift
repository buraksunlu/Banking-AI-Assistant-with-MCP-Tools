import SwiftUI

private struct CustomerTimeZoneKey: EnvironmentKey {
    static let defaultValue: TimeZone? = nil
}

extension EnvironmentValues {
    /// The signed-in customer's time zone (`GET /api/me`); nil until the home screen has loaded it.
    var customerTimeZone: TimeZone? {
        get { self[CustomerTimeZoneKey.self] }
        set { self[CustomerTimeZoneKey.self] = newValue }
    }
}

/// One transaction in full: the amount, the day and local time, the kind, category, merchant, the other side and the bank's
/// own record (IOS-06). Everything is the backend's field; nothing is calculated.
struct TransactionDetailView: View {
    let item: TransactionItem
    @Environment(\.customerTimeZone) private var zone

    var body: some View {
        let shown = TransactionDisplay(item)
        List {
            Section {
                VStack(spacing: 6) {
                    Image(systemName: shown.symbol)
                        .font(.title2)
                        .foregroundStyle(.tint)
                        .frame(width: 52, height: 52)
                        .background(.tint.opacity(0.12), in: Circle())
                    Text(shown.title).font(.headline)
                    Text(shown.amount)
                        .font(.title.bold())
                        .monospacedDigit()
                        .foregroundStyle(shown.isIncoming ? Color.green : Color.primary)
                }
                .frame(maxWidth: .infinity)
                .padding(.vertical, 8)
            }
            Section {
                row("Tarih", dateText)
                row("Tür", kindText)
                if item.kind == .purchase {
                    row("Kategori", item.categoryDisplayName ?? "Sınıflandırılmamış")
                    if let merchant = item.merchant { row("İşyeri", merchant) }
                }
                if let counterparty = item.counterparty {
                    row(item.kind == .transfer ? "Karşı taraf" : "Diğer taraf", counterparty)
                }
                row("Hesap", item.accountName)
                row("Para birimi", item.currencyCode)
            }
            Section("Banka kaydı") {
                Text(item.rawDescription).font(.footnote.monospaced()).textSelection(.enabled)
            }
        }
        .navigationTitle("İşlem detayı")
        .navigationBarTitleDisplayMode(.inline)
    }

    private var dateText: String {
        let day = TransactionDisplay.date(item.localDate)
        guard let zone, let time = TransactionDisplay.time(utc: item.occurredAtUtc, in: zone) else { return day }
        return "\(day) · \(time)"
    }

    private var kindText: String {
        switch item.kind {
        case .purchase: "Harcama"
        case .sale: "Satış"
        case .transfer: item.direction == .credit ? "Gelen transfer" : "Giden transfer"
        case .salary: "Maaş"
        case .openingFunding: "Açılış bakiyesi"
        }
    }

    private func row(_ label: String, _ value: String) -> some View {
        LabeledContent(label) { Text(value).multilineTextAlignment(.trailing) }
    }
}
