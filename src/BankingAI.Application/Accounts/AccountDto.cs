using System.Linq.Expressions;
using BankingAI.Data.Entities;
using BankingAI.Data.Enums;

namespace BankingAI.Application.Accounts;

/// <summary>
/// An account as clients see it. The owner is deliberately absent: it is always the caller (taken from the
/// token), so it is never sent and clients are never tempted to send it back.
/// </summary>
public sealed record AccountDto(
    Guid Id,
    string AccountName,
    string AccountNumber,
    string CurrencyCode,
    AccountType AccountType,
    decimal Balance,
    DateTimeOffset CreatedAt)
{
    /// <summary>
    /// Entity-to-DTO mapping that EF Core translates to SQL, so only these columns are read. Apply ordering
    /// before this <c>Select</c>: EF cannot translate an <c>OrderBy</c> on a constructor-built record.
    /// </summary>
    public static readonly Expression<Func<Account, AccountDto>> Projection = account => new AccountDto(
        account.Id,
        account.AccountName,
        account.AccountNumber,
        account.CurrencyCode,
        account.AccountType,
        account.Balance,
        account.CreatedAt);
}
