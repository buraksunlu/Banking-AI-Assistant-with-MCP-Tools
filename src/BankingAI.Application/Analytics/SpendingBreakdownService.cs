using BankingAI.Application.Investment;
using BankingAI.Data;
using BankingAI.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Analytics;

/// <summary>The input of a spending breakdown: the period and the same optional filters as a chat answer's link.</summary>
public sealed record SpendingBreakdownQuery(
    Guid CustomerId, PeriodKind Period, DateOnly? From = null, DateOnly? To = null, string? CategoryCode = null, string? Merchant = null);

/// <summary>A purchase of the routine group with its reason; <c>Installment</c> is "03/12" for a numbered installment.</summary>
public sealed record RoutineItem(TransactionItem Transaction, RoutineReason Reason, string? Installment);

/// <summary>A non-routine purchase; <c>Eligible</c> is false for an unclassified or "Diğer" purchase, which gets no comparison.</summary>
public sealed record NonRoutineItem(TransactionItem Transaction, bool Eligible);

/// <summary>
/// "Had this been invested" for lira purchases (DEMO-02 K1): every purchase bought on its own day, valued on the customer's today.
/// <c>Amount</c> is what was compared; <c>ExcludedRecent</c> the eligible spending younger than
/// <see cref="SpendingBreakdownRules.MinimumAgeDays"/> days, left out. No assets when everything was too recent.
/// </summary>
public sealed record BreakdownInvestment(
    decimal Amount, decimal ExcludedRecent, string Method, DateOnly ValueDate, IReadOnlyList<AssetWhatIf> Assets, string Disclaimer);

/// <summary>The "Rutin ve temel" group of one currency.</summary>
public sealed record RoutineGroup(decimal Total, int Count, IReadOnlyList<RoutineItem> Items);

/// <summary>The "Rutin olmayan" group of one currency with its comparison and habit advice (lira only).</summary>
public sealed record NonRoutineGroup(
    decimal Total, int Count, IReadOnlyList<NonRoutineItem> Items, BreakdownInvestment? Investment, IReadOnlyList<HabitAdvice> Advice);

/// <summary>One currency's two groups; currencies are never added together.</summary>
public sealed record CurrencyBreakdown(string CurrencyCode, RoutineGroup Routine, NonRoutineGroup NonRoutine);

/// <summary>
/// A period's purchases in two groups (DEMO-02). <c>PeriodTooLong</c> is true, with no currencies, for a period longer than
/// <see cref="SpendingBreakdownRules.MaximumPeriodDays"/> days; the caller shows the plain list instead.
/// </summary>
public sealed record SpendingBreakdown(
    ResolvedPeriod Period, bool PeriodTooLong, MerchantMatch? MerchantMatch, IReadOnlyList<CurrencyBreakdown> Currencies);

/// <summary>
/// One transaction as the breakdown sees it: its routine reason, its spending group and, when it is a non-routine discretionary
/// lira purchase old enough, what it would be worth invested; otherwise <c>Note</c> says why there is no comparison.
/// </summary>
public sealed record TransactionInsight(
    TransactionItem Transaction, RoutineReason? Routine, string? Installment, SpendingGroup Group, BreakdownInvestment? Investment,
    string? Note);

/// <summary>The fixed limits of the breakdown (DEMO-02 K5-A, K6-A).</summary>
public static class SpendingBreakdownRules
{
    /// <summary>Younger purchases are left out of the comparison: a day's change says nothing.</summary>
    public const int MinimumAgeDays = 7;

    /// <summary>The longest period broken down; longer ones stay a plain, paged list.</summary>
    public const int MaximumPeriodDays = 31;

    /// <summary>The comparison's method name in the answer.</summary>
    public const string Method = "EachOnItsOwnDay";

    // Fixed notes of a transaction without a comparison.
    public const string RoutineNote = "Rutin ödeme";
    public const string EssentialNote = "Temel harcama";
    public const string UnknownNote = "Kategorisi belirsiz";
    public const string RecentNote = "Karşılaştırmak için erken";
    public const string LiraOnlyNote = "Yalnız TL harcamalarda";
    public const string PurchaseOnlyNote = "Yalnız harcamalarda";
}

/// <summary>
/// DEMO-02: splits a period's purchases (personal accounts) into "Rutin ve temel" and "Rutin olmayan", compares the non-routine
/// discretionary lira spending with dollars, euros and gold bought on each purchase's own day, and gives at most two habit advices.
/// The backend decides everything; the phone only shows it.
/// </summary>
public sealed class SpendingBreakdownService(BankingDbContext db, PeriodResolver periodResolver, MarketPriceReader prices)
{
    private const string Lira = "TRY";
    // A month has about a hundred purchases at most; the breakdown is one list, so a generous cap only guards against a mistake.
    private const int MaximumRows = 2000;

    private readonly PurchaseScope _purchases = new(db, periodResolver);

    /// <summary>The breakdown of the period's purchases.</summary>
    /// <exception cref="ArgumentException">Invalid period, AllTime, or unknown category code.</exception>
    /// <exception cref="CustomerNotFoundException">The customer does not exist.</exception>
    public async Task<SpendingBreakdown> GetAsync(SpendingBreakdownQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var scoped = await _purchases.ResolveAsync(
            new SpendingQuery(query.CustomerId, query.Period, query.From, query.To), cancellationToken);
        var period = scoped.Period;
        var periodDays = period.EndDate.DayNumber - period.StartDate.DayNumber + 1;
        if (periodDays > SpendingBreakdownRules.MaximumPeriodDays)
            return new SpendingBreakdown(period, true, null, []);

        var purchases = scoped.Purchases;
        if (query.CategoryCode is { } code)
        {
            var categoryId = (await CategoryLookup.FindAsync(db, code, cancellationToken)).Id;
            purchases = purchases.Where(t => t.CategoryId == categoryId);
        }
        MerchantMatch? merchantMatch = null;
        if (query.Merchant is { } merchant)
        {
            var known = await scoped.AccountPurchases.Where(t => t.NormalizedMerchant != null)
                .Select(t => t.NormalizedMerchant!).Distinct().ToListAsync(cancellationToken);
            merchantMatch = MerchantMatcher.Match(merchant, known);
            if (merchantMatch.Status != MerchantMatchStatus.Matched)
                return new SpendingBreakdown(period, false, merchantMatch, []);
            var canonical = merchantMatch.Merchant!;
            purchases = purchases.Where(t => t.NormalizedMerchant == canonical);
        }

        var items = await TransactionItemReader.ReadAsync(db, purchases, TransactionOrder.NewestFirst, 0, MaximumRows,
            query.CustomerId, scoped.TimeZone, cancellationToken);
        var recurring = await RecurringGroupsAsync(scoped.AccountPurchases, items, period.EndDate, scoped.TimeZone, cancellationToken);
        var today = periodResolver.TodayIn(scoped.TimeZone);

        var currencies = new List<CurrencyBreakdown>();
        foreach (var currency in items.GroupBy(i => i.CurrencyCode).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var routine = new List<RoutineItem>();
            var nonRoutine = new List<NonRoutineItem>();
            foreach (var item in currency)
            {
                var isRecurring = item.Merchant is { } m && recurring.Contains((item.AccountId, item.CurrencyCode, m));
                if (RoutineClassifier.Classify(item.RawDescription, item.CategoryCode, isRecurring) is { } reason)
                    routine.Add(new RoutineItem(item, reason, InstallmentText(item.RawDescription)));
                else
                    nonRoutine.Add(new NonRoutineItem(item, SpendingGroups.Of(item.CategoryCode) == SpendingGroup.Discretionary));
            }

            var eligible = nonRoutine.Where(i => i.Eligible).Select(i => i.Transaction).ToList();
            BreakdownInvestment? investment = null;
            IReadOnlyList<HabitAdvice> advice = [];
            if (currency.Key == Lira && eligible.Count > 0)
            {
                investment = await InvestAsync(eligible, today, cancellationToken);
                advice = await AdviseAsync(scoped.AccountPurchases, eligible, period, periodDays, scoped.TimeZone, cancellationToken);
            }
            currencies.Add(new CurrencyBreakdown(currency.Key,
                new RoutineGroup(routine.Sum(i => i.Transaction.Amount), routine.Count, routine),
                new NonRoutineGroup(nonRoutine.Sum(i => i.Transaction.Amount), nonRoutine.Count, nonRoutine, investment, advice)));
        }
        return new SpendingBreakdown(period, false, merchantMatch, currencies);
    }

    /// <summary>One transaction of the customer as the breakdown sees it.</summary>
    /// <exception cref="KeyNotFoundException">
    /// The customer does not exist, or the transaction does not exist or is someone else's; indistinguishable on purpose.
    /// </exception>
    public async Task<TransactionInsight> InsightAsync(Guid customerId, Guid transactionId, CancellationToken cancellationToken = default)
    {
        var item = await new TransactionSearchService(db, periodResolver).GetAsync(customerId, transactionId, cancellationToken);
        var group = SpendingGroups.Of(item.CategoryCode);
        if (item.Kind != TransactionKind.Purchase)
            return new TransactionInsight(item, null, null, group, null, SpendingBreakdownRules.PurchaseOnlyNote);

        var timeZone = await db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId).Select(c => c.TimeZone).SingleAsync(cancellationToken);
        var accountPurchases = db.Transactions.AsNoTracking()
            .Where(t => t.Kind == TransactionKind.Purchase && t.AccountId == item.AccountId);
        // The purchase's own day plays the period's last day: was the merchant a recurring payment when it was paid?
        var recurring = await RecurringGroupsAsync(accountPurchases, [item], item.LocalDate, timeZone, cancellationToken);
        var isRecurring = item.Merchant is { } merchant && recurring.Contains((item.AccountId, item.CurrencyCode, merchant));
        var routine = RoutineClassifier.Classify(item.RawDescription, item.CategoryCode, isRecurring);
        var installment = InstallmentText(item.RawDescription);

        string? note = routine switch
        {
            RoutineReason.Essential => SpendingBreakdownRules.EssentialNote,
            not null => SpendingBreakdownRules.RoutineNote,
            null when group == SpendingGroup.Unknown => SpendingBreakdownRules.UnknownNote,
            null when item.CurrencyCode != Lira => SpendingBreakdownRules.LiraOnlyNote,
            _ => null,
        };
        if (note is not null)
            return new TransactionInsight(item, routine, installment, group, null, note);

        var investment = await InvestAsync([item], periodResolver.TodayIn(timeZone), cancellationToken);
        return new TransactionInsight(item, null, null, group, investment,
            investment.Assets.Count == 0 ? SpendingBreakdownRules.RecentNote : null);
    }

    // The merchant groups (account, currency, merchant) of the items whose payments form a recurring payment over the six months
    // up to lastDay, by the backend's one recurring rule. One query for every merchant of the list.
    private async Task<HashSet<(Guid AccountId, string Currency, string Merchant)>> RecurringGroupsAsync(
        IQueryable<Data.Entities.Transaction> accountPurchases, IReadOnlyCollection<TransactionItem> items, DateOnly lastDay,
        string timeZoneId, CancellationToken cancellationToken)
    {
        var merchants = items.Where(i => i.Merchant is not null).Select(i => i.Merchant!).Distinct().ToList();
        if (merchants.Count == 0)
            return [];
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var startUtc = StartOfDayUtc(lastDay.AddMonths(-6), zone);
        var endUtc = StartOfDayUtc(lastDay.AddDays(1), zone);
        var rows = await accountPurchases
            .Where(t => t.NormalizedMerchant != null && merchants.Contains(t.NormalizedMerchant!)
                && t.OccurredAt >= startUtc && t.OccurredAt < endUtc)
            .Select(t => new { t.AccountId, t.CurrencyCode, Merchant = t.NormalizedMerchant!, t.OccurredAt, t.Amount })
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(r => (r.AccountId, r.CurrencyCode, r.Merchant))
            .Where(g => RecurringPaymentDetector.Detect(
                g.Select(r => new PaymentPoint(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.OccurredAt, zone).DateTime), r.Amount)),
                lastDay) is not null)
            .Select(g => g.Key)
            .ToHashSet();
    }

    // Each eligible purchase old enough is bought on its own day; younger ones are reported as left out.
    private async Task<BreakdownInvestment> InvestAsync(
        IReadOnlyList<TransactionItem> eligible, DateOnly today, CancellationToken cancellationToken)
    {
        var lastBuyDay = today.AddDays(-SpendingBreakdownRules.MinimumAgeDays);
        var old = eligible.Where(i => i.LocalDate <= lastBuyDay).ToList();
        var excluded = eligible.Where(i => i.LocalDate > lastBuyDay).Sum(i => i.Amount);
        IReadOnlyList<AssetWhatIf> assets = [];
        if (old.Count > 0)
        {
            var list = await prices.TryAssetsAsync(cancellationToken);
            var days = old.Select(i => i.LocalDate).Append(today).Distinct().ToList();
            var points = await prices.GetAsync(list.Select(a => a.Id).ToList(), days, cancellationToken);
            assets = list.Select(a => WhatIfMath.Basket(a,
                    old.Select(i => (new BasketBuy(i.LocalDate, i.Amount), points[(a.Id, i.LocalDate)])).ToList(),
                    points[(a.Id, today)]))
                .ToList();
        }
        return new BreakdownInvestment(old.Sum(i => i.Amount), excluded, SpendingBreakdownRules.Method, today, assets,
            WhatIfMath.Disclaimer);
    }

    // K3-A: each eligible merchant against its own six full months before the period's month.
    private async Task<IReadOnlyList<HabitAdvice>> AdviseAsync(
        IQueryable<Data.Entities.Transaction> accountPurchases, IReadOnlyList<TransactionItem> eligible, ResolvedPeriod period,
        int periodDays, string timeZoneId, CancellationToken cancellationToken)
    {
        if (periodDays < HabitAdviceRules.MinimumPeriodDays)
            return [];
        var byMerchant = eligible.Where(i => i.Merchant is not null).GroupBy(i => i.Merchant!).ToList();
        if (byMerchant.Count == 0)
            return [];
        var merchants = byMerchant.Select(g => g.Key).ToList();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var usualEnd = new DateOnly(period.StartDate.Year, period.StartDate.Month, 1);
        var (startUtc, endUtc) = (StartOfDayUtc(usualEnd.AddMonths(-HabitAdviceRules.UsualMonths), zone), StartOfDayUtc(usualEnd, zone));
        var usual = await accountPurchases
            .Where(t => t.CurrencyCode == Lira && t.NormalizedMerchant != null && merchants.Contains(t.NormalizedMerchant!)
                && t.OccurredAt >= startUtc && t.OccurredAt < endUtc)
            .GroupBy(t => t.NormalizedMerchant!)
            .Select(g => new { Merchant = g.Key, Total = g.Sum(t => t.Amount) })
            .ToDictionaryAsync(x => x.Merchant, x => x.Total, cancellationToken);
        return HabitAdviceRules.Evaluate(
            byMerchant.Select(g => new HabitInput(g.Key, g.Count(), g.Sum(i => i.Amount), usual.GetValueOrDefault(g.Key))), periodDays);
    }

    private static string? InstallmentText(string rawDescription) =>
        RoutineClassifier.Installment(rawDescription) is var (number, count) ? $"{number:D2}/{count:D2}" : null;

    private static DateTimeOffset StartOfDayUtc(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
