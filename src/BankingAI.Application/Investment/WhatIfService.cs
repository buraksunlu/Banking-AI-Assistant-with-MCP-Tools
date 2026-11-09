using BankingAI.Application.Analytics;
using BankingAI.Data;
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

/// <summary>
/// Historical "what if" scenarios for the investment arm (todo 12f, K10-B): only past prices, no forecast, no product, no order.
/// Spending amounts come from <see cref="CategoryBreakdownService"/>, so they equal the spending analyses; only TRY spending is
/// used, because every asset is quoted in lira and currencies are never converted.
/// </summary>
public sealed class WhatIfService(
    BankingDbContext db, PeriodResolver periodResolver, CategoryBreakdownService categories, MarketPriceReader prices)
{
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
