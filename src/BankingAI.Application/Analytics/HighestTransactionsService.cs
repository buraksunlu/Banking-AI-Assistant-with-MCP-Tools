using BankingAI.Data;
using BankingAI.Data.Entities;
using BankingAI.Data.Validation;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Analytics;

// Default: the largest purchases on personal accounts (spending). IncludeAllTransactions widens it,
// on the user's explicit request, to every kind on every account of the customer (the statement
// scope of TransactionSearchService). Limit defaults to 5 and may be raised up to 100. The amount
// range is inclusive and needs CurrencyCode, like the search's (ANA-11).
public sealed record HighestTransactionsQuery(
    Guid CustomerId, PeriodKind Period, DateOnly? From = null, DateOnly? To = null, Guid? AccountId = null,
    int Limit = 5, bool IncludeAllTransactions = false,
    decimal? MinAmount = null, decimal? MaxAmount = null, string? CurrencyCode = null);

// Amounts in different currencies are never compared: each currency has its own ranked list.
public sealed record CurrencyHighestTransactions(string CurrencyCode, IReadOnlyList<TransactionItem> Items);

// An empty Currencies list means no matching transaction in the period.
public sealed record HighestTransactions(ResolvedPeriod Period, IReadOnlyList<CurrencyHighestTransactions> Currencies);

public sealed class HighestTransactionsService(BankingDbContext db, PeriodResolver periodResolver)
{
    public const int DefaultLimit = 5;
    public const int MaximumLimit = 100;

    private readonly CustomerScope _customer = new(db, periodResolver);
    private readonly PurchaseScope _purchases = new(db, periodResolver);

    public async Task<HighestTransactions> GetAsync(HighestTransactionsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        InputRules.Require(query.Limit is >= 1 and <= MaximumLimit, nameof(query.Limit), $"1 ile {MaximumLimit} arasında olmalı.");
        AmountFilter.Validate(query.MinAmount, query.MaxAmount, query.CurrencyCode);

        ResolvedPeriod period;
        string timeZoneId;
        IQueryable<Transaction> source;
        if (query.IncludeAllTransactions)
        {
            (period, timeZoneId, var accounts) = await _customer.ResolveAsync(
                query.CustomerId, query.Period, query.From, query.To, query.AccountId, personalOnly: false, cancellationToken);
            source = db.Transactions.AsNoTracking()
                .Where(t => accounts.Contains(t.AccountId) && t.OccurredAt >= period.StartUtc && t.OccurredAt < period.EndUtc);
        }
        else
        {
            // Spending scope: personal accounts and Kind = Purchase (VAL-05).
            var scoped = await _purchases.ResolveAsync(
                new SpendingQuery(query.CustomerId, query.Period, query.From, query.To, query.AccountId), cancellationToken);
            (period, source, timeZoneId) = (scoped.Period, scoped.Purchases, scoped.TimeZone);
        }
        source = AmountFilter.Apply(source, query.MinAmount, query.MaxAmount, query.CurrencyCode);

        // Top-N per currency without a window function: one query for the currencies present, then
        // one ranked query per currency. The count is bounded by the currency whitelist (at most 4),
        // not by the number of rows, so this is not an N+1 pattern.
        var currencyCodes = await source.Select(t => t.CurrencyCode).Distinct().ToListAsync(cancellationToken);
        var currencies = new List<CurrencyHighestTransactions>();
        foreach (var currency in currencyCodes.Order(StringComparer.Ordinal))
        {
            var items = await TransactionItemReader.ReadAsync(
                db, source.Where(t => t.CurrencyCode == currency), TransactionOrder.LargestFirst,
                0, query.Limit, query.CustomerId, timeZoneId, cancellationToken);
            currencies.Add(new CurrencyHighestTransactions(currency, items));
        }

        return new HighestTransactions(period, currencies);
    }
}
