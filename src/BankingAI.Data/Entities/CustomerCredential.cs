namespace BankingAI.Data.Entities;

/// <summary>
/// Login credentials of one customer (one-to-one). Only a salted password hash is stored, never the password.
/// Kept apart from <see cref="Customer"/> so reading a customer never loads security data.
/// </summary>
public sealed class CustomerCredential
{
    public required Guid CustomerId { get; init; }

    /// <summary>Lowercase login name, unique (e.g. "ada").</summary>
    public required string Username { get; init; }

    public string PasswordHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Set by the DbContext to the server time on every insert and update.</summary>
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Replaces the stored hash; the caller hashes the password first.</summary>
    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;
}
