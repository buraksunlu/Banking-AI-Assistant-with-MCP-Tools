using BankingAI.Data;
using BankingAI.Data.Enums;
using BankingAI.Data.Validation;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Analytics;

// A statement-like search: every kind (salary, transfers, sales, purchases) and, by default, all of
// the customer's accounts including a Business shop account. The period is mandatory so a query
// is always bounded; there is deliberately no free-text search.
public sealed record TransactionSearchQuery(
    Guid CustomerId, PeriodKind Period, DateOnly? From = null, DateOnly? To = null, Guid? AccountId = null,
    TransactionKind? Kind = null, TransactionDirection? Direction = null,
    string? CategoryCode = null, bool OnlyUnclassified = false, string? Merchant = null,
    int Limit = 20, int Offset = 0);

// A DTO, never the entity. Counterparty is a masked person ("Deniz M***"), a shop's account name,
// or the customer's own account name (CounterpartyIsOwnAccount); full names never leave the service.
public sealed record TransactionItem(
    Guid Id, Guid AccountId, string AccountName, DateTimeOffset OccurredAtUtc, DateOnly LocalDate,
    TransactionKind Kind, TransactionDirection Direction, decimal Amount, string CurrencyCode,
    string? Merchant, string? CategoryCode, string? CategoryDisplayName, string RawDescription,
    string? Counterparty, bool CounterpartyIsOwnAccount);

// MerchantMatch is set only when a merchant filter was given; Ambiguous/NotFound return no rows.
public sealed record TransactionSearchResult(
    ResolvedPeriod Period, MerchantMatch? MerchantMatch, int TotalCount, bool HasMore, IReadOnlyList<TransactionItem> Items);

public sealed class TransactionSearchService(BankingDbContext db, PeriodResolver periodResolver)
{
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;
    public const int MaximumOffset = 10_000;

    private readonly CustomerScope _scope = new(db, periodResolver);

    public async Task<TransactionSearchResult> SearchAsync(TransactionSearchQuery query, CancellationToken cancellationToken = default)
    {
        Validate(query);
        var (period, timeZoneId, accounts) = await _scope.ResolveAsync(
            query.CustomerId, query.Period, query.From, query.To, query.AccountId, personalOnly: false, cancellationToken);

        var transactions = db.Transactions.AsNoTracking()
            .Where(t => accounts.Contains(t.AccountId) && t.OccurredAt >= period.StartUtc && t.OccurredAt < period.EndUtc);

        if (query.Kind is { } kind)
            transactions = transactions.Where(t => t.Kind == kind);
        if (query.Direction is { } direction)
            transactions = transactions.Where(t => t.Direction == direction);
        if (query.OnlyUnclassified)
            transactions = transactions.Where(t => t.Kind == TransactionKind.Purchase && t.CategoryId == null);
        if (query.CategoryCode is { } code)
        {
            var categoryId = await db.TransactionCategories.AsNoTracking()
                .Where(c => c.Code == code)
                .Select(c => (short?)c.Id)
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new ArgumentException("Tanımlı bir kategori kodu olmalı.", nameof(query));
            transactions = transactions.Where(t => t.CategoryId == categoryId);
        }

        MerchantMatch? merchantMatch = null;
        if (query.Merchant is { } merchant)
        {
            // Candidates: merchants on this scope's accounts across all time (ANA-04 rules).
            var knownMerchants = await db.Transactions.AsNoTracking()
                .Where(t => accounts.Contains(t.AccountId) && t.NormalizedMerchant != null)
                .Select(t => t.NormalizedMerchant!)
                .Distinct()
                .ToListAsync(cancellationToken);
            merchantMatch = MerchantMatcher.Match(merchant, knownMerchants);
            if (merchantMatch.Status != MerchantMatchStatus.Matched)
                return new TransactionSearchResult(period, merchantMatch, 0, false, []);
            var canonical = merchantMatch.Merchant!;
            transactions = transactions.Where(t => t.NormalizedMerchant == canonical);
        }

        var totalCount = await transactions.CountAsync(cancellationToken);

        var items = await TransactionItemReader.ReadAsync(db, transactions, TransactionOrder.NewestFirst,
            query.Offset, query.Limit, query.CustomerId, timeZoneId, cancellationToken);

        return new TransactionSearchResult(period, merchantMatch, totalCount, query.Offset + items.Count < totalCount, items);
    }

    // Every input check runs before the database is touched.
    private static void Validate(TransactionSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        InputRules.Require(query.Limit is >= 1 and <= MaximumLimit, nameof(query.Limit), $"1 ile {MaximumLimit} arasında olmalı.");
        InputRules.Require(query.Offset is >= 0 and <= MaximumOffset, nameof(query.Offset), $"0 ile {MaximumOffset} arasında olmalı.");
        InputRules.Require(query.Kind is null || Enum.IsDefined(query.Kind.Value), nameof(query.Kind), "Tanımlı işlem türü olmalı.");
        InputRules.Require(query.Direction is null || Enum.IsDefined(query.Direction.Value), nameof(query.Direction), "Debit veya Credit olmalı.");
        InputRules.Require(!(query.OnlyUnclassified && query.CategoryCode is not null),
            nameof(query.CategoryCode), "Kategori kodu ile yalnızca sınıflandırılmamış filtresi birlikte kullanılamaz.");
        InputRules.Require((query.CategoryCode is null && !query.OnlyUnclassified) || query.Kind is null or TransactionKind.Purchase,
            nameof(query.Kind), "Kategori filtreleri yalnızca Purchase işlemlerine uygulanır.");
        if (query.CategoryCode is not null)
            InputRules.Text(query.CategoryCode, 50, nameof(query.CategoryCode));
        if (query.Merchant is not null)
            InputRules.Text(query.Merchant, MerchantMatcher.MaximumLength, nameof(query.Merchant));
    }
}
