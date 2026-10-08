namespace BankingAI.Data.Entities;

public sealed class DailyMarketPrice
{
    public long Id { get; private set; }
    public required short MarketAssetId { get; init; }
    public required DateOnly PriceDate { get; init; }
    public required decimal Price { get; init; }
    public string? Source { get; init; }
}
