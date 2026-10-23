using BankingAI.Data;
using BankingAI.Data.Enums;
using BankingAI.Data.Validation;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Analytics;

// A statement-like search: every kind (salary, transfers, sales, purchases) and, by default, all of
// the customer's accounts including a Business shop account. The period is mandatory; AllTime (API-17)
// is the whole history up to today, asked for by name. There is deliberately no free-text search.
// MinAmount/MaxAmount are inclusive and need CurrencyCode (ANA-11): a threshold is never compared
// across currencies. Cursor is the previous page's NextCursor; without it the newest page comes.
// AccountType narrows the accounts (CHAT-07: a chat answer counts Personal accounts only, so its link asks for the
// same); it intersects with AccountId, and the merchant candidates follow the same narrowed scope.
public sealed record TransactionSearchQuery(
    Guid CustomerId, PeriodKind Period, DateOnly? From = null, DateOnly? To = null, Guid? AccountId = null,
    TransactionKind? Kind = null, TransactionDirection? Direction = null,
    string? CategoryCode = null, bool OnlyUnclassified = false, string? Merchant = null,
    int Limit = 20, string? Cursor = null,
    decimal? MinAmount = null, decimal? MaxAmount = null, string? CurrencyCode = null, AccountType? AccountType = null);

// A DTO, never the entity. Counterparty is a masked person ("Deniz M***"), a shop's account name,
// or the customer's own account name (CounterpartyIsOwnAccount); full names never leave the service.
public sealed record TransactionItem(
    Guid Id, Guid AccountId, string AccountName, DateTimeOffset OccurredAtUtc, DateOnly LocalDate,
    TransactionKind Kind, TransactionDirection Direction, decimal Amount, string CurrencyCode,
    string? Merchant, string? CategoryCode, string? CategoryDisplayName, string RawDescription,
    string? Counterparty, bool CounterpartyIsOwnAccount);

// MerchantMatch is set only when a merchant filter was given; Ambiguous/NotFound return no rows.
// TotalCount is the whole filtered result on every page; NextCursor is null on the last page.
public sealed record TransactionSearchResult(
    ResolvedPeriod Period, MerchantMatch? MerchantMatch, int TotalCount, bool HasMore, string? NextCursor,
    IReadOnlyList<TransactionItem> Items);

public sealed class TransactionSearchService(BankingDbContext db, PeriodResolver periodResolver)
{
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;

    private readonly CustomerScope _scope = new(db, periodResolver);

    public async Task<TransactionSearchResult> SearchAsync(TransactionSearchQuery query, CancellationToken cancellationToken = default)
    {
        Validate(query);
        var cursor = query.Cursor is null ? null : TransactionCursor.Parse(query.Cursor, nameof(query.Cursor));
        var (timeZoneId, accounts) = await _scope.ResolveAccountsAsync(
            query.CustomerId, query.AccountId, personalOnly: false, cancellationToken);
        if (query.AccountType is { } accountType)
            accounts = accounts.Where(id => db.Accounts.Any(a => a.Id == id && a.AccountType == accountType));
        var allTime = query.Period == PeriodKind.AllTime;
        // The whole history has no lower bound; its start is known only after counting, so until then it is today.
        var period = allTime
            ? periodResolver.ResolveAllTime(timeZoneId, oldestUtc: null)
            : periodResolver.Resolve(query.Period, timeZoneId, query.From, query.To);

        var transactions = db.Transactions.AsNoTracking()
            .Where(t => accounts.Contains(t.AccountId) && t.OccurredAt < period.EndUtc);
        if (!allTime)
            transactions = transactions.Where(t => t.OccurredAt >= period.StartUtc);

        if (query.Kind is { } kind)
            transactions = transactions.Where(t => t.Kind == kind);
        if (query.Direction is { } direction)
            transactions = transactions.Where(t => t.Direction == direction);
        if (query.OnlyUnclassified)
            transactions = transactions.Where(t => t.Kind == TransactionKind.Purchase && t.CategoryId == null);
        transactions = AmountFilter.Apply(transactions, query.MinAmount, query.MaxAmount, query.CurrencyCode);
        if (query.CategoryCode is { } code)
        {
            var categoryId = (await CategoryLookup.FindAsync(db, code, cancellationToken)).Id;
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
                return new TransactionSearchResult(period, merchantMatch, 0, false, null, []);
            var canonical = merchantMatch.Merchant!;
            transactions = transactions.Where(t => t.NormalizedMerchant == canonical);
        }

        // One query for the count and, for AllTime, the oldest moment that starts the period. No rows, no group.
        var totals = await transactions
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Oldest = g.Min(t => t.OccurredAt) })
            .SingleOrDefaultAsync(cancellationToken);
        if (allTime)
            period = periodResolver.ResolveAllTime(timeZoneId, totals?.Oldest);

        // Keyset paging: rows strictly after the cursor in (OccurredAt, Id) order, the order of the list itself. Rows
        // added meanwhile are newer, so they can no longer push a row of the previous page onto this one.
        var page = cursor is null ? transactions
            : transactions.Where(t => EF.Functions.LessThan(
                ValueTuple.Create(t.OccurredAt, t.Id), ValueTuple.Create(cursor.OccurredAtUtc, cursor.Id)));
        // One row more than asked tells whether another page follows without a second count.
        var rows = await TransactionItemReader.ReadAsync(db, page, TransactionOrder.NewestFirst,
            0, query.Limit + 1, query.CustomerId, timeZoneId, cancellationToken);
        var hasMore = rows.Count > query.Limit;
        var items = hasMore ? rows[..query.Limit] : rows;

        return new TransactionSearchResult(period, merchantMatch, totals?.Count ?? 0, hasMore,
            hasMore ? TransactionCursor.After(items[^1]) : null, items);
    }

    /// <summary>
    /// One transaction of the customer by id, in the same shape as a statement row. Any kind and any of the
    /// customer's accounts (a Business shop account included); no period limit.
    /// </summary>
    /// <exception cref="ArgumentException">The customer id is empty.</exception>
    /// <exception cref="KeyNotFoundException">
    /// The customer does not exist, or the transaction does not exist or sits on someone else's account; the
    /// last two are indistinguishable so another customer's transaction never leaks its existence.
    /// </exception>
    public async Task<TransactionItem> GetAsync(Guid customerId, Guid transactionId, CancellationToken cancellationToken = default)
    {
        var (timeZoneId, accounts) = await _scope.ResolveAccountsAsync(
            customerId, accountId: null, personalOnly: false, cancellationToken);

        // Ownership is part of the same query: a foreign transaction simply yields no row.
        var transaction = db.Transactions.AsNoTracking()
            .Where(t => t.Id == transactionId && accounts.Contains(t.AccountId));

        var items = await TransactionItemReader.ReadAsync(db, transaction, TransactionOrder.NewestFirst,
            skip: 0, take: 1, customerId, timeZoneId, cancellationToken);
        return items.SingleOrDefault() ?? throw new KeyNotFoundException("İşlem bulunamadı.");
    }

    // Every input check runs before the database is touched.
    private static void Validate(TransactionSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        InputRules.Require(query.Limit is >= 1 and <= MaximumLimit, nameof(query.Limit), $"1 ile {MaximumLimit} arasında olmalı.");
        InputRules.Require(query.Period != PeriodKind.AllTime || (query.From is null && query.To is null),
            nameof(query.From), "Başlangıç/bitiş tarihi yalnızca Custom dönemde verilebilir.");
        InputRules.Require(query.Kind is null || Enum.IsDefined(query.Kind.Value), nameof(query.Kind), "Tanımlı işlem türü olmalı.");
        InputRules.Require(query.AccountType is null || Enum.IsDefined(query.AccountType.Value), nameof(query.AccountType), "Personal veya Business olmalı.");
        InputRules.Require(query.Direction is null || Enum.IsDefined(query.Direction.Value), nameof(query.Direction), "Debit veya Credit olmalı.");
        InputRules.Require(!(query.OnlyUnclassified && query.CategoryCode is not null),
            nameof(query.CategoryCode), "Kategori kodu ile yalnızca sınıflandırılmamış filtresi birlikte kullanılamaz.");
        InputRules.Require((query.CategoryCode is null && !query.OnlyUnclassified) || query.Kind is null or TransactionKind.Purchase,
            nameof(query.Kind), "Kategori filtreleri yalnızca Purchase işlemlerine uygulanır.");
        if (query.CategoryCode is not null)
            InputRules.Text(query.CategoryCode, 50, nameof(query.CategoryCode));
        if (query.Merchant is not null)
            InputRules.Text(query.Merchant, MerchantMatcher.MaximumLength, nameof(query.Merchant));
        AmountFilter.Validate(query.MinAmount, query.MaxAmount, query.CurrencyCode);
    }
}
