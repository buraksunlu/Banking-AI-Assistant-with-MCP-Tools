namespace BankingAI.Application.Investment;

/// <summary>
/// One asset's "what if": the buy and valuation days, the prices used, the units bought and their value today. Figures are null
/// unless both prices are usable; <c>Status</c> is then the worse of the two. <c>Source</c> is the valuation price's source.
/// </summary>
public sealed record AssetWhatIf(
    string Symbol, string Name, string BaseUnit, DateOnly BuyDate, DateOnly? BuyPriceDate, decimal? BuyPrice, DateOnly ValueDate,
    DateOnly? ValuePriceDate, decimal? ValuePrice, decimal? Units, decimal? Value, decimal? Difference, decimal? DifferencePercent,
    PriceStatus Status, string? Source);

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

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
