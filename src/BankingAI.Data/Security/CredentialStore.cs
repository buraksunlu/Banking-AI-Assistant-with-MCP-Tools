using BankingAI.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Data.Security;

public enum CredentialChange
{
    Created = 1,
    Updated = 2
}

/// <summary>
/// Creates a customer's login or changes its password. Conflicts are errors, never silent overwrites.
/// </summary>
public static class CredentialStore
{
    /// <summary>
    /// Sets <paramref name="password"/> for <paramref name="username"/>. A new login needs <paramref name="customerId"/>;
    /// for an existing login it may be omitted, and if given it must match the login's customer.
    /// </summary>
    /// <exception cref="ArgumentException">Invalid username or password.</exception>
    /// <exception cref="InvalidOperationException">Missing customer, or the login/customer pairing conflicts.</exception>
    public static async Task<CredentialChange> SetPasswordAsync(
        BankingDbContext db, string username, string password, Guid? customerId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        var normalized = PasswordCredentials.NormalizeUsername(username ?? "");
        if (!PasswordCredentials.IsValidUsername(normalized))
            throw new ArgumentException("Kullanıcı adı 3–50 karakter; küçük harf, rakam, nokta, tire veya alt çizgi olmalı.", nameof(username));
        // Validate and hash before touching the database.
        var hash = PasswordCredentials.Hash(password);

        var existing = await db.CustomerCredentials.SingleOrDefaultAsync(c => c.Username == normalized, cancellationToken);
        if (existing is not null)
        {
            if (customerId is not null && customerId != existing.CustomerId)
                throw new InvalidOperationException($"'{normalized}' başka bir müşteriye ait.");
            existing.SetPasswordHash(hash);
            await db.SaveChangesAsync(cancellationToken);
            return CredentialChange.Updated;
        }

        if (customerId is not { } id)
            throw new InvalidOperationException($"'{normalized}' için kayıt yok; yeni kullanıcı için müşteri kimliği gerekli.");
        if (!await db.Customers.AnyAsync(c => c.Id == id, cancellationToken))
            throw new InvalidOperationException("Müşteri bulunamadı.");
        if (await db.CustomerCredentials.AnyAsync(c => c.CustomerId == id, cancellationToken))
            throw new InvalidOperationException("Bu müşterinin zaten bir kullanıcı adı var; şifresi o kullanıcı adıyla değiştirilmeli.");

        var credential = new CustomerCredential { CustomerId = id, Username = normalized };
        credential.SetPasswordHash(hash);
        db.CustomerCredentials.Add(credential);
        await db.SaveChangesAsync(cancellationToken);
        return CredentialChange.Created;
    }
}
