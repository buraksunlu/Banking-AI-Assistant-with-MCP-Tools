namespace BankingAI.Application.Investment;

/// <summary>
/// One asset's "what if": the buy and valuation days, the prices used, the units bought and their value today. Figures are null
/// unless both prices are usable; <c>Status</c> is then the worse of the two. <c>Source</c> is the valuation price's source.
/// </summary>
public sealed record AssetWhatIf(
    string Symbol, string Name, string BaseUnit, DateOnly BuyDate, DateOnly? BuyPriceDate, decimal? BuyPrice, DateOnly ValueDate,
    DateOnly? ValuePriceDate, decimal? ValuePrice, decimal? Units, decimal? Value, decimal? Difference, decimal? DifferencePercent,
    PriceStatus Status, string? Source);

/// <summary>One monthly buy: the day, the price used and the units bought. Units are null unless the whole asset has figures.</summary>
public sealed record MonthlyBuy(DateOnly BuyDate, DateOnly? PriceDate, decimal? Price, decimal? Units, PriceStatus Status);

/// <summary>A month's price change from its first day to the next month's first day (<c>Month</c> is the month's first day).</summary>
public sealed record MonthChange(DateOnly Month, decimal ChangePercent);

/// <summary>
/// The largest fall of the price from a peak to a later trough over the monthly points and today, with the price days. Zero and
/// no days when the price never fell below an earlier point.
/// </summary>
public sealed record Drawdown(decimal Percent, DateOnly? PeakDate, decimal? PeakPrice, DateOnly? TroughDate, decimal? TroughPrice);

/// <summary>The saving itself on one day: what was invested so far, what it was worth and the return on the invested amount.</summary>
public sealed record RunningReturn(DateOnly Date, decimal Invested, decimal Value, decimal ReturnPercent);

/// <summary>
/// Past volatility of one asset (OC-05, K16-C): the price's worst and best month and its largest fall, and the worst moment of the
/// saving itself, which is milder because every month buys again.
/// </summary>
public sealed record AssetVolatility(MonthChange WorstMonth, MonthChange BestMonth, Drawdown MaxDrawdown, RunningReturn WorstMoment);

/// <summary>
/// One asset's monthly saving (OC-04): the twelve buys, the units and their value today, and the volatility. Figures and
/// volatility are null unless every price is usable; <c>Status</c> is then the worst one (K18-A). <c>Invested</c> is always shown.
/// </summary>
public sealed record MonthlyAssetWhatIf(
    string Symbol, string Name, string BaseUnit, decimal Invested, DateOnly ValueDate, DateOnly? ValuePriceDate, decimal? ValuePrice,
    decimal? Units, decimal? Value, decimal? Difference, decimal? DifferencePercent, PriceStatus Status, string? Source,
    IReadOnlyList<MonthlyBuy> Buys, AssetVolatility? Volatility);

/// <summary>
/// The shared engine of every scenario (OC-01): lira → units at the buy price → value at the valuation price. One value per day,
/// so buying and selling use the same price (K3-A). Units stay exact; only reported figures are rounded.
/// </summary>
public static class WhatIfMath
{
    /// <summary>The fixed warning returned with every result (K11-A, OC-09).</summary>
    public const string Disclaimer =
        "Geçmiş fiyatlarla yapılmış bir hesaptır; gelecekteki getiriyi göstermez ve yatırım tavsiyesi değildir. "
        + "Alış-satış farkı, vergi, komisyon, faiz ve enflasyon dahil değildir.";

    /// <summary>Units (6 decimals), value, difference and difference percent (2 decimals) of one purchase.</summary>
    public static (decimal Units, decimal Value, decimal Difference, decimal DifferencePercent) OneOff(
        decimal amount, decimal buyPrice, decimal valuePrice)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(buyPrice);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(valuePrice);
        // Rounding the units first would move the value by up to half a unit's price (about 3 TL for a gram of gold).
        var units = amount / buyPrice;
        var value = units * valuePrice;
        return (Math.Round(units, 6, MidpointRounding.AwayFromZero), Round(value), Round(value - amount),
            Round((value - amount) * 100m / amount));
    }

    /// <summary>The row of one asset for <paramref name="amount"/> bought on <paramref name="buy"/>'s day.</summary>
    public static AssetWhatIf Asset(MarketAssetInfo asset, decimal amount, PricePoint buy, PricePoint value)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(buy);
        ArgumentNullException.ThrowIfNull(value);
        var status = (PriceStatus)Math.Max((int)buy.Status, (int)value.Status);
        decimal? units = null, worth = null, difference = null, percent = null;
        // A stale price is shown with its day but never turned into figures (K5-D).
        if (status == PriceStatus.Ok)
            (units, worth, difference, percent) = OneOff(amount, buy.Price!.Value, value.Price!.Value);
        return new AssetWhatIf(asset.Symbol, asset.Name, asset.BaseUnit, buy.RequestedDate, buy.PriceDate, buy.Price,
            value.RequestedDate, value.PriceDate, value.Price, units, worth, difference, percent, status, value.Source ?? buy.Source);
    }

    /// <summary>The fewest monthly buys (INV-03 K10): one buy is the one-off scenario, and one month has no volatility to show.</summary>
    public const int MinimumMonths = 2;

    /// <summary>
    /// The most monthly buys (INV-03 K1-A): prices start on 11 November 2024, so 1 December 2024 is the earliest first with a
    /// price; at the demo moment that is 22 months back, and later days only reach back less far.
    /// </summary>
    public const int MaximumMonths = 22;

    /// <summary>
    /// <paramref name="amount"/> bought on each of the <paramref name="buys"/> (one per month, <see cref="MinimumMonths"/> to
    /// <see cref="MaximumMonths"/>), valued at <paramref name="value"/>. <paramref name="monthAfter"/> is the first day after the
    /// last buy's month; it closes the last month's change.
    /// </summary>
    public static MonthlyAssetWhatIf Monthly(
        MarketAssetInfo asset, decimal amount, IReadOnlyList<PricePoint> buys, PricePoint monthAfter, PricePoint value)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(buys);
        ArgumentNullException.ThrowIfNull(monthAfter);
        ArgumentNullException.ThrowIfNull(value);
        if (buys.Count is < MinimumMonths or > MaximumMonths)
            throw new ArgumentException($"Birikim {MinimumMonths} ile {MaximumMonths} arası aylık alım ister.", nameof(buys));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        var months = buys.Count;
        var invested = amount * months;
        var status = buys.Append(monthAfter).Append(value).Max(p => p.Status);
        if (status != PriceStatus.Ok)
            // K18-A: a saving with a missing month is not "twelve months of saving"; days and prices are still shown.
            return new MonthlyAssetWhatIf(asset.Symbol, asset.Name, asset.BaseUnit, invested, value.RequestedDate, value.PriceDate,
                value.Price, null, null, null, null, status, value.Source,
                buys.Select(b => new MonthlyBuy(b.RequestedDate, b.PriceDate, b.Price, null, b.Status)).ToList(), null);

        var units = buys.Select(b => amount / b.Price!.Value).ToList();
        var total = units.Sum();
        var worth = total * value.Price!.Value;
        return new MonthlyAssetWhatIf(asset.Symbol, asset.Name, asset.BaseUnit, invested, value.RequestedDate, value.PriceDate,
            value.Price, Math.Round(total, 6, MidpointRounding.AwayFromZero), Round(worth), Round(worth - invested),
            Round((worth - invested) * 100m / invested), status, value.Source,
            buys.Select((b, i) => new MonthlyBuy(b.RequestedDate, b.PriceDate, b.Price,
                Math.Round(units[i], 6, MidpointRounding.AwayFromZero), b.Status)).ToList(),
            Volatility(amount, buys, units, monthAfter, value));
    }

    // Every input price is usable here; comparisons use exact values, only reported figures are rounded.
    private static AssetVolatility Volatility(
        decimal amount, IReadOnlyList<PricePoint> buys, IReadOnlyList<decimal> units, PricePoint monthAfter, PricePoint value)
    {
        var firsts = buys.Append(monthAfter).ToList();
        var months = buys.Count;
        var changes = Enumerable.Range(0, months)
            .Select(i => (Month: firsts[i].RequestedDate, Change: (firsts[i + 1].Price!.Value - firsts[i].Price!.Value) * 100m / firsts[i].Price!.Value))
            .ToList();
        // The earliest month wins a tie, so the answer does not depend on sort stability.
        var worst = changes.Aggregate((a, b) => b.Change < a.Change ? b : a);
        var best = changes.Aggregate((a, b) => b.Change > a.Change ? b : a);

        // Drawdown on the price points the saving actually sees: every first and today.
        var points = firsts.Append(value).ToList();
        var peak = points[0];
        (decimal Percent, PricePoint? Peak, PricePoint? Trough) fall = (0m, null, null);
        foreach (var point in points)
        {
            if (point.Price > peak.Price)
                peak = point;
            var percent = (point.Price!.Value - peak.Price!.Value) * 100m / peak.Price!.Value;
            if (percent < fall.Percent)
                fall = (percent, peak, point);
        }

        // The saving on each first before that month's buy (from the second month on) and today, after every buy.
        var moments = Enumerable.Range(1, months - 1)
            .Select(i => (Day: firsts[i].RequestedDate, Invested: amount * i, Value: units.Take(i).Sum() * firsts[i].Price!.Value))
            .Append((Day: monthAfter.RequestedDate, Invested: amount * months, Value: units.Sum() * monthAfter.Price!.Value))
            .Append((Day: value.RequestedDate, Invested: amount * months, Value: units.Sum() * value.Price!.Value))
            .ToList();
        var low = moments.Aggregate((a, b) => (b.Value - b.Invested) / b.Invested < (a.Value - a.Invested) / a.Invested ? b : a);

        return new AssetVolatility(
            new MonthChange(worst.Month, Round(worst.Change)),
            new MonthChange(best.Month, Round(best.Change)),
            new Drawdown(Round(fall.Percent), fall.Peak?.PriceDate, fall.Peak?.Price, fall.Trough?.PriceDate, fall.Trough?.Price),
            new RunningReturn(low.Day, Round(low.Invested), Round(low.Value), Round((low.Value - low.Invested) * 100m / low.Invested)));
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
