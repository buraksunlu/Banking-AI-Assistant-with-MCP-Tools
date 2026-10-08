using BankingAI.Data.Enums;

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
            if (value <= 0m)
                throw new ArgumentOutOfRangeException(nameof(Amount), value, "Amount must be positive.");

            _amount = value;
        }
    }

    public required TransactionDirection Direction { get; init; }
    public required string CurrencyCode { get; init; }
    public required string RawDescription { get; init; }
    public string? NormalizedMerchant { get; set; }
    public CategorizationSource? CategorizationSource { get; set; }
    public decimal? CategoryConfidence { get; set; }
    public DateTimeOffset? CategorizedAt { get; set; }
    public required DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
