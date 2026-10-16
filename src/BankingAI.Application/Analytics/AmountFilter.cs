using BankingAI.Data.Entities;
using BankingAI.Data.Validation;

namespace BankingAI.Application.Analytics;

/// <summary>
/// The optional amount range of the transaction lists (ANA-11): inclusive bounds in one currency. Shared by
/// <see cref="TransactionSearchService"/> and <see cref="HighestTransactionsService"/> so both apply the same rules.
/// </summary>
internal static class AmountFilter
{
    /// <summary>
    /// Checks the range before the database is touched. Bounds follow the money rule; a bound needs a currency,
    /// because a threshold compared across currencies (500 TRY against 500 USD) would be meaningless.
    /// </summary>
    /// <exception cref="ArgumentException">An invalid bound or currency, a bound without a currency, or min above max.</exception>
    public static void Validate(decimal? min, decimal? max, string? currency)
    {
        if (currency is not null)
            InputRules.Currency(currency, nameof(TransactionSearchQuery.CurrencyCode));
        if (min is { } minimum)
            InputRules.Money(minimum, nameof(TransactionSearchQuery.MinAmount), positive: true);
        if (max is { } maximum)
            InputRules.Money(maximum, nameof(TransactionSearchQuery.MaxAmount), positive: true);
        InputRules.Require((min is null && max is null) || currency is not null,
            nameof(TransactionSearchQuery.CurrencyCode), "Tutar filtresi için para birimi (currencyCode) verilmeli.");
        InputRules.Require(min is null || max is null || min <= max,
            nameof(TransactionSearchQuery.MinAmount), "En az tutar en çok tutardan büyük olamaz.");
    }

    /// <summary>Narrows an unexecuted query to the currency and the inclusive range; it stays a single SQL query.</summary>
    public static IQueryable<Transaction> Apply(IQueryable<Transaction> transactions, decimal? min, decimal? max, string? currency)
    {
        if (currency is not null)
            transactions = transactions.Where(t => t.CurrencyCode == currency);
        if (min is { } minimum)
            transactions = transactions.Where(t => t.Amount >= minimum);
        if (max is { } maximum)
            transactions = transactions.Where(t => t.Amount <= maximum);
        return transactions;
    }
}
