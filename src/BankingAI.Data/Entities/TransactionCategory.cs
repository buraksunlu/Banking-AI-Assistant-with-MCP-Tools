namespace BankingAI.Data.Entities;

public sealed class TransactionCategory
{
    public required short Id { get; init; }
    public required string Code { get; init; }
    public required string DisplayName { get; set; }
}
