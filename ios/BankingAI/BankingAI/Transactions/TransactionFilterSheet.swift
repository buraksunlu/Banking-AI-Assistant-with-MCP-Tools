import SwiftUI

/// A separate filter draft; only a successful Apply changes the displayed statement.
struct TransactionFilterSheet: View {
    let model: TransactionHistoryViewModel
    @Environment(\.dismiss) private var dismiss
    @State private var choice: Int
    @State private var from: Date
    @State private var to: Date
    @State private var isApplying = false
    @State private var applyTask: Task<Void, Never>?

    init(model: TransactionHistoryViewModel) {
        self.model = model
        _choice = State(initialValue: TransactionFilter.presets.firstIndex(of: model.filter) ?? 5)
        let start: String?
        let end: String?
        if case .custom(let a, let b) = model.filter { start = a; end = b }
        else { start = model.period?.startDate; end = model.period?.endDate }
        let fallback = Date()
        _from = State(initialValue: start.flatMap { TransactionFilter.date($0) } ?? fallback)
        _to = State(initialValue: end.flatMap { TransactionFilter.date($0) } ?? fallback)
    }

    private var draft: TransactionFilter {
        choice < TransactionFilter.presets.count ? TransactionFilter.presets[choice] :
            .custom(from: TransactionFilter.day(from), to: TransactionFilter.day(to))
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Picker("Dönem", selection: $choice) {
                        ForEach(Array(TransactionFilter.presets.enumerated()), id: \.offset) { index, filter in
                            Text(filter.title).tag(index)
                        }
                        Text("Özel tarih aralığı").tag(5)
                    }
                    if choice == 5 {
                        DatePicker("Başlangıç", selection: $from, displayedComponents: .date)
                        DatePicker("Bitiş", selection: $to, displayedComponents: .date)
                    }
                } footer: {
                    Text("Başlangıç ve bitiş günleri dahildir. Özel aralık en fazla 24 ay olabilir. Gelecek tarihler için işlem listelenemez.")
                }
                if let message = draft.validationMessage ?? model.filterError {
                    Section {
                        Label(message, systemImage: "exclamationmark.triangle")
                            .foregroundStyle(.red).accessibilityIdentifier("filterError")
                    }
                }
                if isApplying { Section { ProgressView("Seçilen dönem yükleniyor…") } }
            }
            .disabled(isApplying)
            .navigationTitle("İşlem dönemi")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Vazgeç") {
                        applyTask?.cancel()
                        dismiss()
                    }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Uygula") {
                        let selection = draft
                        isApplying = true
                        applyTask = Task {
                            let applied = await model.apply(selection)
                            isApplying = false
                            if applied { dismiss() }
                        }
                    }
                    .disabled(isApplying || draft.validationMessage != nil)
                }
            }
            .onChange(of: draft) { model.clearFilterError() }
        }
        // Date pickers carry calendar days, never the phone's local instants.
        .environment(\.calendar, TransactionFilter.calendar)
        .environment(\.timeZone, TimeZone(secondsFromGMT: 0)!)
        .environment(\.locale, Locale(identifier: "tr_TR"))
        .onDisappear { applyTask?.cancel() }
    }
}
