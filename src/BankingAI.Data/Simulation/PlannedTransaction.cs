using BankingAI.Data.Entities;
using BankingAI.Data.Enums;

namespace BankingAI.Data.Simulation;

/// <summary>
/// One transaction of a day plan (RT-02a): what happens, on which account and when. A transfer names its receiving account
/// (<see cref="CounterpartId"/>) and becomes two legs of one pair; so does a purchase at a bank customer's shop (RT-05a), whose
/// other leg is the shop's sale. Everything else is one leg. Identifiers come from the plan's
/// name, so the same day planned again gives the same rows and a second write is a harmless replay.
/// </summary>
public sealed record PlannedTransaction(
    Guid Id,
    Guid AccountId,
    decimal Amount,
    string Currency,
    TransactionKind Kind,
    string RawDescription,
    string? Merchant,
    short? CategoryId,
    DateTimeOffset At,
    Guid? CounterpartId = null,
    Guid? CounterpartLegId = null,
    Guid? PairId = null)
{
    /// <summary>The rows the writers take: one for a purchase or an income, the debit and the credit of a transfer.</summary>
    public IReadOnlyList<Transaction> ToTransactions()
    {
        var source = CategoryId is null ? (CategorizationSource?)null : CategorizationSource.Rule;
        if (CounterpartId is not { } counterpart)
        {
            var direction = Kind == TransactionKind.Purchase ? TransactionDirection.Debit : TransactionDirection.Credit;
            return [Row(Id, AccountId, direction, Kind, RawDescription, Merchant, CategoryId, source)];
        }
        if (Kind == TransactionKind.Purchase)
            return
            [
                Row(Id, AccountId, TransactionDirection.Debit, Kind, RawDescription, Merchant, CategoryId, source),
                Row(CounterpartLegId!.Value, counterpart, TransactionDirection.Credit, TransactionKind.Sale, $"{Merchant} satış geliri", null,
                    null, null),
            ];
        return
        [
            Row(Id, AccountId, TransactionDirection.Debit, Kind, RawDescription, null, null, null),
            Row(CounterpartLegId!.Value, counterpart, TransactionDirection.Credit, Kind, "Gelen para transferi", null, null, null),
        ];
    }

    private Transaction Row(Guid id, Guid account, TransactionDirection direction, TransactionKind kind, string raw, string? merchant,
        short? category, CategorizationSource? source) => new()
    {
        Id = id,
        AccountId = account,
        Amount = Amount,
        Direction = direction,
        Kind = kind,
        PairId = PairId,
        CurrencyCode = Currency,
        RawDescription = raw,
        NormalizedMerchant = merchant,
        CategoryId = category,
        CategorizationSource = source,
        CategorizedAt = source is null ? null : DateTimeOffset.UtcNow,
        OccurredAt = At,
    };
}
