using BankingAI.Data.Enums;
using BankingAI.Data.Validation;

namespace BankingAI.Data.Entities;

public sealed class Transaction
{
    private Guid _accountId;
    private decimal _amount;

    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid AccountId
    {
        get => _accountId;
        init
        {
            if (value == Guid.Empty)
                throw new ArgumentException("AccountId must not be empty.", nameof(AccountId));

            _accountId = value;
        }
    }

    public short? CategoryId { get; set; }

    public required decimal Amount
    {
        get => _amount;
        init
        {
            InputRules.Money(value, nameof(Amount), positive: true);

            _amount = value;
        }
    }

    public required TransactionDirection Direction { get; init; }
    public required TransactionKind Kind { get; init; }
    // Transfer ve iç dükkân alışverişinin iki bacağı aynı PairId'yi taşır.
    public Guid? PairId { get; init; }
    public required string CurrencyCode { get; init; }
    public required string RawDescription { get; init; }
    public string? NormalizedMerchant { get; set; }
    public CategorizationSource? CategorizationSource { get; set; }
    public decimal? CategoryConfidence { get; set; }
    public DateTimeOffset? CategorizedAt { get; set; }
    public required DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public void SetCategorization(short categoryId, CategorizationSource source, decimal? confidence = null)
    {
        var now = DateTimeOffset.UtcNow;
        InputRules.Require(Kind == TransactionKind.Purchase, nameof(Kind), "Yalnızca Purchase kategorize edilir.");
        InputRules.Require(CategorizationSource != Enums.CategorizationSource.Manual || source == Enums.CategorizationSource.Manual,
            nameof(CategorizationSource), "Manuel kategori otomatik yöntemle ezilemez.");
        InputRules.Categorization(categoryId, source, confidence, now);
        InputRules.Timestamp(CreatedAt, now, nameof(CreatedAt));
        // Validate first so failure leaves all four previous values untouched.
        CategoryId = categoryId;
        CategorizationSource = source;
        CategoryConfidence = confidence;
        CategorizedAt = now;
    }

    public void ClearCategorization()
    {
        CategoryId = null;
        CategorizationSource = null;
        CategoryConfidence = null;
        CategorizedAt = null;
    }
}
