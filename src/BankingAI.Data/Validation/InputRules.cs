using System.Buffers;
using System.Text;

namespace BankingAI.Data.Validation;

// Pure C# checks; no database access and no automatic correction of input.
public static class InputRules
{
    public const decimal MaximumMoney = 9999999999999999.99m;

    public static void Require(bool condition, string field, string reason)
    {
        if (!condition)
            throw new ArgumentException(reason, field);
    }

    // The supported currencies, by exact code (the Accounts/Transactions CHECK constraints hold the same list).
    public static void Currency(string? value, string field) =>
        Require(value is "TRY" or "USD" or "EUR" or "GBP", field, "TRY/USD/EUR/GBP olmalı.");

    public static void Money(decimal value, string field, bool positive)
    {
        Require(value >= (positive ? 0.01m : 0m) && value <= MaximumMoney,
            field, positive ? "Tutar 0.01 ile parasal üst sınır arasında olmalı." : "Bakiye negatif olamaz veya parasal üst sınırı aşamaz.");
        Require(decimal.Round(value, 2) == value, field, "En fazla iki anlamlı ondalık basamak kullanılabilir.");
    }

    public static void Text(string? value, int maximum, string field, bool optional = false)
    {
        if (value is null && optional) return;
        Require(!string.IsNullOrWhiteSpace(value), field, "Metin null, boş veya yalnızca whitespace olamaz.");
        var remaining = value.AsSpan();
        var count = 0;
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out var rune, out var consumed);
            Require(status == OperationStatus.Done && rune.Value != 0, field, "Geçerli Unicode metin olmalı; NUL içeremez.");
            remaining = remaining[consumed..];
            count++;
        }
        Require(count <= maximum, field, $"En fazla {maximum} Unicode karakter kullanılabilir.");
    }

    public static void Timestamp(DateTimeOffset value, DateTimeOffset referenceUtc, string field)
    {
        Require(value.Offset == TimeSpan.Zero, field, "UTC, offset 0 olmalı.");
        Require(value > DateTimeOffset.MinValue && value < DateTimeOffset.MaxValue,
            field, "Default/MinValue/MaxValue tarih kullanılamaz.");
        Require(value <= referenceUtc, field, "Gelecekte olamaz.");
    }

    // KIND-01 table: which direction, pair and category each economic kind may carry.
    public static void Kind(Enums.TransactionKind kind, Enums.TransactionDirection direction, Guid? pairId, short? categoryId)
    {
        Require(Enum.IsDefined(kind), "Kind", "Tanımlı işlem türü olmalı.");
        Require(pairId != Guid.Empty, "PairId", "Boş GUID kullanılamaz.");
        Require(kind switch
        {
            Enums.TransactionKind.Purchase => direction == Enums.TransactionDirection.Debit,
            Enums.TransactionKind.Transfer => true,
            _ => direction == Enums.TransactionDirection.Credit
        }, "Kind", "İşlem türü bu yönle kullanılamaz.");
        Require(kind switch
        {
            Enums.TransactionKind.Sale or Enums.TransactionKind.Transfer => pairId is not null,
            Enums.TransactionKind.Salary or Enums.TransactionKind.OpeningFunding => pairId is null,
            _ => true
        }, "PairId", "Transfer/Sale çift kimliği ister; Salary/OpeningFunding çift taşımaz.");
        Require(kind == Enums.TransactionKind.Purchase || categoryId is null,
            "CategoryId", "Yalnızca Purchase harcama kategorisi taşır.");
    }

    public static void Categorization(short? categoryId, Enums.CategorizationSource? source,
        decimal? confidence, DateTimeOffset? categorizedAt)
    {
        if (categoryId is null)
        {
            Require(source is null && confidence is null && categorizedAt is null,
                "CategoryId", "Kategorisiz işlemde dört kategori alanı da null olmalı.");
            return;
        }

        Require(categoryId > 0 && categorizedAt is not null, "CategoryId", "Pozitif kategori ve atama tarihi gerekli.");
        Require(source is Enums.CategorizationSource.Rule or Enums.CategorizationSource.Llm or Enums.CategorizationSource.Manual,
            "CategorizationSource", "Geçerli atama yöntemi gerekli.");
        if (source == Enums.CategorizationSource.Llm)
        {
            Require(confidence is >= 0m and <= 1m, "CategoryConfidence", "LLM skoru 0–1 arasında zorunlu.");
            Require(decimal.Round(confidence!.Value, 4) == confidence.Value,
                "CategoryConfidence", "Skor en fazla dört anlamlı ondalık içerebilir.");
        }
        else
            Require(confidence is null, "CategoryConfidence", "Rule/Manual için skor null olmalı.");
    }
}
