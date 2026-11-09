using BankingAI.Application.Analytics;
using BankingAI.Data;
using BankingAI.Data.Validation;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Investment;

/// <summary>
/// Input of <see cref="WhatIfService.OneOffAsync"/>: a spending period and, optionally, one category. The customer comes from the
/// trusted caller context (token), never from a model argument.
/// </summary>
public sealed record OneOffWhatIfQuery(
    Guid CustomerId, PeriodKind Period, DateOnly? From = null, DateOnly? To = null, string? CategoryCode = null);

/// <summary>
/// "Had I not spent it" (OC-03): the period's lira spending bought on the period's first day and valued today. No asset rows
/// without lira spending.
/// </summary>
public sealed record OneOffWhatIf(
    ResolvedPeriod Period, string? CategoryCode, string? CategoryDisplayName, string CurrencyCode, decimal SpentAmount,
    DateOnly BuyDate, DateOnly ValueDate, string Method, string Disclaimer, IReadOnlyList<AssetWhatIf> Assets);

/// <summary>Where the monthly amount comes from (K13-B).</summary>
public enum WhatIfAmountSource
{
    /// <summary>The amount the user gave.</summary>
    User,

    /// <summary>The monthly saving of INC-02's advice for the last complete month, read by the backend.</summary>
    Advice,
}

/// <summary>Whether INC-02 gave an amount for <see cref="WhatIfAmountSource.Advice"/>.</summary>
public enum AdviceAmountStatus
{
    /// <summary>The lira advice's monthly savings were used.</summary>
    Used,

    /// <summary>No lira advice for the last complete month: no amount, no asset rows (K20-A).</summary>
    NoAdvice,
}

/// <summary>
/// Input of <see cref="WhatIfService.MonthlyAsync"/>: either a lira amount with its currency, or <c>FromAdvice</c> (never both).
/// </summary>
public sealed record MonthlyWhatIfQuery(Guid CustomerId, decimal? Amount = null, string? CurrencyCode = null, bool FromAdvice = false);

/// <summary>
/// "Had I saved it every month" (OC-04): the amount bought on the first day of each of the last twelve complete months and valued
/// today, with each asset's volatility. <c>AdvicePeriod</c> is the advice month when the amount comes from INC-02.
/// <c>Amount</c> and <c>Invested</c> are null, and there are no asset rows, when there is no advice.
/// </summary>
public sealed record MonthlyWhatIf(
    WhatIfAmountSource AmountSource, AdviceAmountStatus? AdviceStatus, ResolvedPeriod? AdvicePeriod, string CurrencyCode,
    decimal? Amount, int Months, DateOnly FirstBuyDate, DateOnly LastBuyDate, DateOnly ValueDate, decimal? Invested, string Method,
    string Disclaimer, IReadOnlyList<MonthlyAssetWhatIf> Assets);

/// <summary>
/// Historical "what if" scenarios for the investment arm (todo 12f, K10-B): only past prices, no forecast, no product, no order.
/// Spending amounts come from <see cref="CategoryBreakdownService"/>, so they equal the spending analyses; only TRY spending is
/// used, because every asset is quoted in lira and currencies are never converted.
/// </summary>
public sealed class WhatIfService(
    BankingDbContext db, PeriodResolver periodResolver, CategoryBreakdownService categories, SpendingAdviceService advice,
    MarketPriceReader prices)
{
    /// <summary>The assumption of <see cref="MonthlyAsync"/>.</summary>
    public const string MonthlyMethod = "MonthlyBuyOnFirstDay";

    /// <summary>The largest monthly amount accepted (K14-A).</summary>
    public const decimal MaximumMonthlyAmount = 1_000_000m;

    /// <summary>The checks of a monthly query, before any database work.</summary>
    /// <exception cref="ArgumentException">A missing, doubled or invalid amount or currency.</exception>
    public static void ValidateMonthly(MonthlyWhatIfQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        // K13-B: exactly one source, never a silent default, so a forgotten amount is visible.
        InputRules.Require(query.Amount is not null || query.FromAdvice, nameof(query.Amount),
            "Aylık tutar zorunlu: amount ile currencyCode ya da fromAdvice=true verilmeli.");
        InputRules.Require(query.Amount is null || !query.FromAdvice, nameof(query.Amount),
            "Tutar (amount) ile fromAdvice birlikte verilemez.");
        if (query.FromAdvice)
        {
            InputRules.Require(query.CurrencyCode is null, nameof(query.CurrencyCode), "fromAdvice ile para birimi verilmez; öneri tutarı TL'dir.");
            return;
        }

        // K15-B: a user's amount always names its currency, so "100 euro" is never read as lira.
        InputRules.Require(query.CurrencyCode is not null, nameof(query.CurrencyCode), "Tutarın para birimi (currencyCode) verilmeli.");
        InputRules.Currency(query.CurrencyCode, nameof(query.CurrencyCode));
        InputRules.Require(query.CurrencyCode == Currency, nameof(query.CurrencyCode),
            "Birikim senaryosu yalnız TL (TRY) tutarla hesaplanır; varlık fiyatları TL'dir.");
        InputRules.Money(query.Amount!.Value, nameof(query.Amount), positive: true);
        InputRules.Require(query.Amount <= MaximumMonthlyAmount, nameof(query.Amount), "Aylık tutar en fazla 1.000.000 olmalı.");
    }

    /// <summary>The monthly saving of the last twelve complete months, valued on the customer's today (OC-04/05).</summary>
    /// <exception cref="ArgumentException">See <see cref="ValidateMonthly"/>.</exception>
    /// <exception cref="CustomerNotFoundException">The customer does not exist.</exception>
    public async Task<MonthlyWhatIf> MonthlyAsync(MonthlyWhatIfQuery query, CancellationToken cancellationToken = default)
    {
        ValidateMonthly(query);
        var (timeZone, _) = await new CustomerScope(db, periodResolver)
            .ResolveAccountsAsync(query.CustomerId, accountId: null, personalOnly: true, cancellationToken);
        var today = periodResolver.TodayIn(timeZone);
        // K8-A: the twelve complete months before the running one; the running month's first day closes the last month.
        var monthAfter = new DateOnly(today.Year, today.Month, 1);
        var buyDays = Enumerable.Range(0, WhatIfMath.Months).Select(i => monthAfter.AddMonths(i - WhatIfMath.Months)).ToList();

        var (source, status, advicePeriod, amount) = (WhatIfAmountSource.User, (AdviceAmountStatus?)null, (ResolvedPeriod?)null, query.Amount);
        if (query.FromAdvice)
        {
            // The same advice the customer sees for the last complete month; only lira advice, as the assets are lira.
            var result = await advice.GetAsync(new SpendingAdviceQuery(query.CustomerId, PeriodKind.LastMonth), cancellationToken);
            var saving = result.Currencies.Where(c => c.CurrencyCode == Currency).SelectMany(c => c.Advice).Sum(a => a.MonthlySaving);
            (source, advicePeriod) = (WhatIfAmountSource.Advice, result.Period);
            (status, amount) = saving > 0m ? (AdviceAmountStatus.Used, saving) : (AdviceAmountStatus.NoAdvice, (decimal?)null);
        }

        IReadOnlyList<MonthlyAssetWhatIf> assets = [];
        if (amount is { } monthly)
        {
            var list = await prices.TryAssetsAsync(cancellationToken);
            var points = await prices.GetAsync(list.Select(a => a.Id).ToList(), [.. buyDays, monthAfter, today], cancellationToken);
            // Fixed asset order, no "winner" (OC-07).
            assets = list.Select(a => WhatIfMath.Monthly(a, monthly, buyDays.Select(d => points[(a.Id, d)]).ToList(),
                points[(a.Id, monthAfter)], points[(a.Id, today)])).ToList();
        }

        return new MonthlyWhatIf(source, status, advicePeriod, Currency, amount, WhatIfMath.Months, buyDays[0], buyDays[^1], today,
            amount * WhatIfMath.Months, MonthlyMethod, WhatIfMath.Disclaimer, assets);
    }

    /// <summary>The assumption of <see cref="OneOffAsync"/>.</summary>
    public const string OneOffMethod = "SingleBuyAtPeriodStart";

    /// <summary>Lira currency code of the spending used.</summary>
    public const string Currency = "TRY";

    /// <summary>The period's (and category's) lira spending bought on the period's first day, valued on the customer's today.</summary>
    /// <exception cref="ArgumentException">Invalid period or unknown category code.</exception>
    /// <exception cref="CustomerNotFoundException">The customer does not exist.</exception>
    public async Task<OneOffWhatIf> OneOffAsync(OneOffWhatIfQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        // Customer, period and account rules are the spending analyses' own.
        var breakdown = await categories.GetAsync(
            new SpendingQuery(query.CustomerId, query.Period, query.From, query.To), cancellationToken);
        var category = query.CategoryCode is { } code ? await CategoryLookup.FindAsync(db, code, cancellationToken) : null;

        var lira = breakdown.Currencies.FirstOrDefault(c => c.CurrencyCode == Currency);
        var spent = lira is null ? 0m
            : category is null ? lira.TotalAmount
            : lira.Categories.Where(c => c.CategoryCode == category.Code).Sum(c => c.Amount);

        // "Today" is the customer's business day (demo moment), as everywhere else; the breakdown has already found the customer.
        var timeZone = await db.Customers.AsNoTracking()
            .Where(c => c.Id == query.CustomerId).Select(c => c.TimeZone).SingleAsync(cancellationToken);
        var (buyDay, valueDay) = (breakdown.Period.StartDate, periodResolver.TodayIn(timeZone));

        IReadOnlyList<AssetWhatIf> assets = [];
        if (spent > 0m)
        {
            var list = await prices.TryAssetsAsync(cancellationToken);
            var points = await prices.GetAsync(list.Select(a => a.Id).ToList(), [buyDay, valueDay], cancellationToken);
            // Fixed asset order, no "winner" (OC-07).
            assets = list.Select(a => WhatIfMath.Asset(a, spent, points[(a.Id, buyDay)], points[(a.Id, valueDay)])).ToList();
        }

        return new OneOffWhatIf(breakdown.Period, category?.Code, category?.DisplayName, Currency, spent, buyDay, valueDay,
            OneOffMethod, WhatIfMath.Disclaimer, assets);
    }
}
