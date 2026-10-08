namespace BankingAI.Data.Entities;

public sealed class Customer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    // PostgreSQL generates this value when the row is inserted.
    public string CustomerNumber { get; private set; } = null!;
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string PreferredCurrency { get; init; } = "TRY";
    public string TimeZone { get; init; } = "Europe/Istanbul";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
