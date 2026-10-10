using BankingAI.Data.Enums;

namespace BankingAI.Data.Entities;

public sealed class Account
{
    private Guid _customerId;

    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid CustomerId
    {
        get => _customerId;
        init
        {
            if (value == Guid.Empty)
                throw new ArgumentException("CustomerId must not be empty.", nameof(CustomerId));

            _customerId = value;
        }
    }

    public required string AccountName { get; set; }
    public required string AccountNumber { get; init; }
    public required string CurrencyCode { get; init; }
    // Most accounts are personal; the dummy shop account sets Business explicitly.
    public AccountType AccountType { get; init; } = AccountType.Personal;
    public decimal Balance { get; private set; } = 0m;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
