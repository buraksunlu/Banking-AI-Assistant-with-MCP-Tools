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
    /// The transaction's place in the breakdown (DEMO-02); nil leaves the screen as it was.
    var insight: TransactionInsightViewModel? = nil
    @Environment(\.customerTimeZone) private var zone

    var body: some View {
        let shown = TransactionDisplay(item)
        ScrollView {
            VStack(alignment: .leading, spacing: Layout.large) {
                VStack(spacing: Layout.small) {
                    Image(systemName: shown.symbol)
                        .font(.title2)
                        .foregroundStyle(Palette.secondaryText.color)
                        .frame(width: 56, height: 56)
                        .background(Palette.raised.color, in: Circle())
                        .accessibilityHidden(true)
                    Text(shown.title)
                        .font(Typography.title)
                        .foregroundStyle(Palette.primaryText.color)
                        .multilineTextAlignment(.center)
                    Text(shown.amount)
                        .font(Typography.balance)
                        .foregroundStyle(shown.isIncoming ? Palette.incoming.color : Palette.primaryText.color)
                        .lineLimit(1)
                        .minimumScaleFactor(0.6)
                }
                .frame(maxWidth: .infinity)
                .accessibilityElement(children: .combine)

                VStack(spacing: 0) {
                    ForEach(Array(rows.enumerated()), id: \.offset) { index, row in
                        if index > 0 { HairlineDivider() }
                        DetailRow(label: row.0, value: row.1)
                    }
                }
                .padding(.horizontal, Layout.regular)
                .cardSurface(padding: 0)

                if let insight { InsightSection(model: insight) }

                VStack(alignment: .leading, spacing: Layout.small) {
                    SectionTitle(text: "Banka kaydı")
                    Text(item.rawDescription)
                        .font(.footnote.monospaced())
                        .foregroundStyle(Palette.primaryText.color)
                        .textSelection(.enabled)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .cardSurface()
                }
            }
            .padding(.horizontal, Layout.regular)
            .padding(.top, Layout.small)
        }
        .blackScreen()
        .navigationTitle("İşlem detayı")
        .navigationBarTitleDisplayMode(.inline)
        .task { await insight?.load() }
    }

    /// The label and value of every line of the information card; a line the backend has no field for is left out.
    private var rows: [(String, String)] {
        var rows = [("Tarih", dateText), ("Tür", TransactionDisplay.kindText(item))]
        if item.kind == .purchase {
            rows.append(("Kategori", item.categoryDisplayName ?? "Sınıflandırılmamış"))
            if let merchant = item.merchant { rows.append(("İşyeri", merchant)) }
        }
        if let counterparty = item.counterparty {
            rows.append((item.kind == .transfer ? "Karşı taraf" : "Diğer taraf", counterparty))
        }
        rows.append(("Hesap", item.accountName))
        rows.append(("Para birimi", item.currencyCode))
        return rows
    }

    private var dateText: String {
        let day = TransactionDisplay.date(item.localDate)
        guard let zone, let time = TransactionDisplay.time(utc: item.occurredAtUtc, in: zone) else { return day }
        return "\(day) · \(time)"
    }
}

/// The detail's breakdown part (DEMO-02): the routine tag, then the comparison or the backend's note on why there is none.
private struct InsightSection: View {
    let model: TransactionInsightViewModel

    var body: some View {
        switch model.state {
        case .loading:
            Text("Yükleniyor")
                .font(Typography.caption)
                .frame(maxWidth: .infinity, alignment: .leading)
                .cardSurface()
                .skeleton()
        case .failed(let message):
            StatusBand(text: message, actionTitle: "Tekrar dene") { Task { await model.load() } }
        case .loaded(let insight):
            VStack(alignment: .leading, spacing: Layout.small) {
                if let tag = BreakdownDisplay.insightTag(insight) {
                    Text(tag)
                        .font(Typography.eyebrow)
                        .foregroundStyle(Palette.accent.color)
                }
                if let investment = insight.investment {
                    InvestmentCard(title: BreakdownDisplay.singleInvestmentTitle, investment: investment)
                } else if let note = insight.note {
                    Text(note)
                        .font(Typography.caption)
                        .foregroundStyle(Palette.secondaryText.color)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .cardSurface()
                }
            }
        }
    }
}
