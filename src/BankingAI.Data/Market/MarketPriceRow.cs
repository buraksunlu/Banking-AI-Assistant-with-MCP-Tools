namespace BankingAI.Data.Market;

/// <summary>
/// One daily value for a market asset, read from a source and not yet written to the database.
/// </summary>
/// <param name="Symbol">Market asset symbol, for example <c>USDTRY</c>.</param>
/// <param name="PriceDate">The source's publication day.</param>
/// <param name="Price">Value of one base unit in the quote currency.</param>
/// <param name="Source">Short source name stored with the price.</param>
public sealed record MarketPriceRow(string Symbol, DateOnly PriceDate, decimal Price, string Source);
