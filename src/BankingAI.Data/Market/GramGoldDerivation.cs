namespace BankingAI.Data.Market;

/// <summary>
/// Derives the TRY value of one gram of gold: USD per troy ounce × the same day's USDTRY ÷ grams per troy ounce.
/// A reference value, not a jeweller's price (IMP-b, user decision K1-B).
/// </summary>
public static class GramGoldDerivation
{
    /// <summary>Grams in one troy ounce (exact by definition).</summary>
    public const decimal GramsPerTroyOunce = 31.1034768m;

    /// <summary>Market asset symbol the derived value is stored under.</summary>
    public const string Symbol = "GRAM_GOLD_TRY";

    /// <summary>Source stored with every derived row: gold-api ounce × TCMB USDTRY.</summary>
    public const string Source = "GOLDAPI+TCMB";

    /// <summary>Ounce move above this share (10%) is written but listed for a look (K6).</summary>
    public const decimal WatchChange = 0.10m;

    /// <summary>Ounce move above this share (25%) is not written and is listed as suspicious (K6).</summary>
    public const decimal SuspiciousChange = 0.25m;

    /// <summary>TRY per gram, rounded explicitly to the table's 8 decimals (half to even).</summary>
    public static decimal GramPrice(decimal usdPerOunce, decimal usdTry) =>
        // The quotient has more decimals than numeric(20,8) holds; rounding here keeps it explicit, not silent.
        Math.Round(usdPerOunce * usdTry / GramsPerTroyOunce, 8, MidpointRounding.ToEven);

    /// <summary>
    /// Derives one row per USDTRY day. A day without an ounce price is reported, never filled in; each ounce is
    /// compared with the last accepted one, so a single bad day does not make the next good day look suspicious.
    /// </summary>
    public static GoldDerivation Derive(IReadOnlyList<OuncePrice> ounces, IReadOnlyDictionary<DateOnly, decimal> usdTry)
    {
        var ounceByDay = ounces.ToDictionary(o => o.Day, o => o.UsdPerOunce);
        var rows = new List<MarketPriceRow>();
        var missing = new List<DateOnly>();
        var watch = new List<OunceJump>();
        var suspicious = new List<OunceJump>();
        (DateOnly Day, decimal Ounce)? lastAccepted = null;

        // Only USDTRY (TCMB publication) days: the three assets share one calendar and no day is invented (K3-A).
        foreach (var (day, usd) in usdTry.OrderBy(p => p.Key))
        {
            if (!ounceByDay.TryGetValue(day, out var ounce))
            {
                missing.Add(day);
                continue;
            }

            if (lastAccepted is { } previous)
            {
                // The ounce, not the TRY result, is checked: lira shocks arrive through TCMB's official USDTRY and are real.
                var change = ounce / previous.Ounce - 1m;
                var jump = new OunceJump(day, previous.Day, change);
                if (Math.Abs(change) > SuspiciousChange)
                {
                    suspicious.Add(jump);
                    continue;
                }

                if (Math.Abs(change) > WatchChange)
                    watch.Add(jump);
            }

            rows.Add(new MarketPriceRow(Symbol, day, GramPrice(ounce, usd), Source));
            lastAccepted = (day, ounce);
        }

        return new GoldDerivation(rows, missing, watch, suspicious);
    }
}

/// <summary>Result of a derivation run.</summary>
/// <param name="Rows">Rows to import.</param>
/// <param name="MissingOunceDays">USDTRY days that had no ounce price.</param>
/// <param name="Watch">Written days whose ounce moved more than <see cref="GramGoldDerivation.WatchChange"/>.</param>
/// <param name="Suspicious">Days not written because the ounce moved more than <see cref="GramGoldDerivation.SuspiciousChange"/>.</param>
public sealed record GoldDerivation(
    IReadOnlyList<MarketPriceRow> Rows,
    IReadOnlyList<DateOnly> MissingOunceDays,
    IReadOnlyList<OunceJump> Watch,
    IReadOnlyList<OunceJump> Suspicious);

/// <summary>An ounce move between a day and the last accepted day before it; <paramref name="Change"/> is a share (−0.1128 = −11.28%).</summary>
public sealed record OunceJump(DateOnly Day, DateOnly ComparedWith, decimal Change);
