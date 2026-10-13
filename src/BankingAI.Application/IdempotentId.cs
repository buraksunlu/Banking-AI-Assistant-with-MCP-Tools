using System.Security.Cryptography;
using System.Text;
using BankingAI.Data.Validation;

namespace BankingAI.Application;

/// <summary>
/// Turns a client's <c>Idempotency-Key</c> into the id of the resource it creates (API-12). The same customer,
/// resource and key always give the same id, so a retried request finds the row it already wrote instead of
/// writing a second one; different customers using the same key never collide.
/// </summary>
public static class IdempotentId
{
    public const int MaximumKeyLength = 100;

    public const string Transaction = "transaction";
    public const string Account = "account";

    /// <summary>The id for this customer's resource created under <paramref name="key"/>.</summary>
    /// <exception cref="ArgumentException">The key is not 1–100 visible ASCII characters.</exception>
    public static Guid For(string resource, Guid customerId, string key)
    {
        // Visible ASCII only (no spaces): what clients generate (a UUID) and safe to compare byte for byte.
        InputRules.Require(key is { Length: >= 1 and <= MaximumKeyLength } && key.All(c => c is >= '!' and <= '~'),
            "idempotencyKey", $"1–{MaximumKeyLength} görünür ASCII karakter olmalı (boşluk yok).");

        // A hash of the three parts; the first 16 bytes become the id, marked as an RFC 9562 version 8 ("custom")
        // UUID with the RFC variant. The line breaks keep "a" + "bc" and "ab" + "c" apart.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{resource}\n{customerId:N}\n{key}"));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}
