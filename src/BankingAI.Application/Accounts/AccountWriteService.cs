using BankingAI.Application.Analytics;
using BankingAI.Data;
using BankingAI.Data.Entities;
using BankingAI.Data.Enums;
using BankingAI.Data.Writing;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Accounts;

/// <summary>
/// What a customer may choose when opening an account. Everything else is decided by the backend.
/// </summary>
public sealed record NewAccount(string AccountName, string CurrencyCode);

/// <summary>The opened account, and whether this call was a retry that found it already opened.</summary>
public sealed record AccountWriteResult(AccountDto Account, bool Replayed);

/// <summary>
/// Opens accounts for a customer. Controllers depend on this contract rather than on the class.
/// </summary>
public interface IAccountWriteService
{
    /// <summary>
    /// Opens a personal account with a zero balance and a new "ACC-n" number, and returns it exactly as
    /// <see cref="IAccountReadService.GetAsync"/> would. With an idempotency key, a retry of the same request returns
    /// the account it already opened instead of opening a second one.
    /// </summary>
    /// <exception cref="ArgumentException">Empty customer id, or a name or currency the account rules reject.</exception>
    /// <exception cref="KeyNotFoundException">The customer does not exist.</exception>
    /// <exception cref="IdempotencyConflictException">The key already opened a different account.</exception>
    Task<AccountWriteResult> CreateAsync(Guid customerId, NewAccount account, string? idempotencyKey = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IAccountWriteService"/> on PostgreSQL.
/// </summary>
public sealed class AccountWriteService(BankingDbContext db, PeriodResolver periodResolver, IAccountReadService reader)
    : IAccountWriteService
{
    // Customer lookup goes through the same place as every read, so "customer not found" means the same everywhere.
    private readonly CustomerScope _customer = new(db, periodResolver);

    /// <inheritdoc />
    public async Task<AccountWriteResult> CreateAsync(Guid customerId, NewAccount account, string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        Guid? id = idempotencyKey is null ? null : IdempotentId.For(IdempotentId.Account, customerId, idempotencyKey);
        await _customer.ResolveAccountsAsync(customerId, accountId: null, personalOnly: false, cancellationToken);

        // Optional key (decision 2-A): a retry finds the account it already opened.
        if (id is { } existingId && await ReplayAsync(customerId, existingId, account, cancellationToken) is { } replay)
            return replay;

        // The sequence hands out each number exactly once, even to concurrent requests, so no retry or lock
        // is needed. Taking it before the insert keeps the entity complete for the C# validation.
        var number = await db.Database
            .SqlQueryRaw<long>($"SELECT nextval('\"{BankingDbContext.AccountNumberSequence}\"') AS \"Value\"")
            .SingleAsync(cancellationToken);

        // Type, balance and creation time are never taken from the caller: always Personal, always 0, and
        // CreatedAt is set to server time by SaveChanges. Name and currency are validated there as well.
        var entity = new Account
        {
            Id = id ?? Guid.NewGuid(),
            CustomerId = customerId,
            AccountName = account.AccountName,
            AccountNumber = $"ACC-{number}",
            CurrencyCode = account.CurrencyCode,
            AccountType = AccountType.Personal
        };
        db.Accounts.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (ArgumentException)
        {
            // Leave the context clean for the caller; the rejected account must not be saved by a later call.
            db.Entry(entity).State = EntityState.Detached;
            throw;
        }
        catch (DbUpdateException error) when (id is not null && error.InnerException is Npgsql.PostgresException
                                              { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent retry with the same key opened it between our check and our insert.
            db.Entry(entity).State = EntityState.Detached;
            return await ReplayAsync(customerId, entity.Id, account, cancellationToken) ?? throw new IdempotencyConflictException();
        }

        return new AccountWriteResult(await reader.GetAsync(customerId, entity.Id, cancellationToken), Replayed: false);
    }

    // The account this key already opened: the same request is a replay, a different one a conflict; none yet is null.
    private async Task<AccountWriteResult?> ReplayAsync(Guid customerId, Guid id, NewAccount account, CancellationToken cancellationToken)
    {
        var stored = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new { a.CustomerId, a.AccountName, a.CurrencyCode })
            .SingleOrDefaultAsync(cancellationToken);
        if (stored is null)
            return null;
        if (stored.CustomerId != customerId || stored.AccountName != account.AccountName || stored.CurrencyCode != account.CurrencyCode)
            throw new IdempotencyConflictException();
        return new AccountWriteResult(await reader.GetAsync(customerId, id, cancellationToken), Replayed: true);
    }
}
