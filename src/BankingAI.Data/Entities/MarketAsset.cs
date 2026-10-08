namespace BankingAI.Data.Entities;

public sealed class MarketAsset
{
    public required short Id { get; init; }
    public required string Symbol { get; init; }
    public required string Name { get; set; }
    public required string AssetType { get; init; }
    public required string BaseUnit { get; init; }
    public required string QuoteCurrency { get; init; }
    public required bool IsActive { get; set; }
}
