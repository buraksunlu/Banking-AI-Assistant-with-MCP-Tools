using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;

namespace BankingAI.Data.Security;

/// <summary>
/// Username rules and password hashing for <see cref="Entities.CustomerCredential"/>. Passwords are hashed
/// with ASP.NET Core's <see cref="PasswordHasher{TUser}"/> (salted, slow PBKDF2); the password itself is never stored.
/// </summary>
public static partial class PasswordCredentials
{
    public const int MinimumPasswordLength = 8;
    public const int MaximumPasswordLength = 128;

    // The hasher ignores the user object; one shared instance is thread-safe.
    private static readonly PasswordHasher<object> Hasher = new();
    private static readonly object User = new();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{2,49}$")]
    private static partial Regex UsernamePattern();

    /// <summary>Trims and lowercases, so "Ada " and "ada" are the same login.</summary>
    public static string NormalizeUsername(string username) => username.Trim().ToLowerInvariant();

    /// <summary>Checks an already normalized username (same rule as the database CHECK).</summary>
    public static bool IsValidUsername(string? username) => username is not null && UsernamePattern().IsMatch(username);

    /// <exception cref="ArgumentException">The password is shorter than 8 or longer than 128 characters.</exception>
    public static string Hash(string password)
    {
        if (password is null || password.Length is < MinimumPasswordLength or > MaximumPasswordLength)
            throw new ArgumentException($"Şifre {MinimumPasswordLength}–{MaximumPasswordLength} karakter olmalı.", nameof(password));
        return Hasher.HashPassword(User, password);
    }

    /// <summary>True when <paramref name="password"/> matches the stored hash.</summary>
    public static bool Verify(string passwordHash, string password) =>
        Hasher.VerifyHashedPassword(User, passwordHash, password) is not PasswordVerificationResult.Failed;
}
