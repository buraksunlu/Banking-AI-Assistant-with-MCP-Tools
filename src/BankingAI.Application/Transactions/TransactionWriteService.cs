using BankingAI.Application.Analytics;
using BankingAI.Data;
using BankingAI.Data.Entities;
using BankingAI.Data.Enums;
using BankingAI.Data.Writing;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Transactions;

/// <summary>
/// What a customer may say about a new transaction: only the event itself. Direction, currency, time and every
/// category field are decided by the backend.
/// </summary>
public sealed record NewTransaction(TransactionKind Kind, decimal Amount, string Description, string? Merchant);

/// <summary>
/// The written transaction, and whether this call was a retry that found it already written (nothing moved again).
/// </summary>
public sealed record TransactionWriteResult(TransactionItem Transaction, bool Replayed);

/// <summary>
/// Adds synthetic transactions to a customer's account. Controllers depend on this contract rather than on the
/// class. Never exposed to the LLM or MCP: those stay read-only.
/// </summary>
public interface ITransactionWriteService
{
    /// <summary>
    /// Adds a Purchase (money out) or a Salary (money in) to one of the customer's accounts and changes the
    /// balance in the same database transaction, then returns the transaction exactly as
    /// <see cref="TransactionSearchService.GetAsync"/> would.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Empty customer id, a kind other than Purchase/Salary, a Salary with a merchant, a missing or malformed
    /// idempotency key, or values the transaction rules reject (amount, description, merchant).
    /// </exception>
    /// <exception cref="KeyNotFoundException">
    /// The customer does not exist, or the account does not exist or belongs to someone else.
    /// </exception>
    /// <exception cref="BalanceLimitException">
    /// The balance would drop below zero or rise above the money ceiling; nothing was written.
    /// </exception>
    /// <exception cref="IdempotencyConflictException">
    /// The key was already used for a different transaction (another amount, account, ...); nothing was written.
    /// </exception>
    Task<TransactionWriteResult> CreateAsync(Guid customerId, Guid accountId, NewTransaction transaction,
        string? idempotencyKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="ITransactionWriteService"/> on PostgreSQL, through the atomic <see cref="TransactionWriter"/>.
/// </summary>
public sealed class TransactionWriteService(
    BankingDbContext db, PeriodResolver periodResolver, TimeProvider clock, TransactionSearchService reader)
    : ITransactionWriteService
{
    private readonly CustomerScope _scope = new(db, periodResolver);

    /// <inheritdoc />
    public async Task<TransactionWriteResult> CreateAsync(Guid customerId, Guid accountId, NewTransaction transaction,
        string? idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        // Transfers and sales need both legs and opening funding is seed-only, so only these two map to a direction.
        var direction = transaction.Kind switch
        {
            TransactionKind.Purchase => TransactionDirection.Debit,
            TransactionKind.Salary => TransactionDirection.Credit,
            _ => throw new ArgumentException("Yalnız Purchase veya Salary eklenebilir.", nameof(transaction.Kind))
        };
        if (transaction.Kind == TransactionKind.Salary && transaction.Merchant is not null)
            throw new ArgumentException("Maaş işleminde mağaza adı olmaz.", nameof(transaction.Merchant));
        // Required for money (decision 2-A): without it a retried request would silently move money twice.
        if (idempotencyKey is null)
            throw new ArgumentException("Zorunlu alan.", nameof(idempotencyKey));
        var id = IdempotentId.For(IdempotentId.Transaction, customerId, idempotencyKey);

        // Same ownership rule as every read: a foreign account looks exactly like a missing one.
        var (_, accounts) = await _scope.ResolveAccountsAsync(customerId, accountId, personalOnly: false, cancellationToken);
        var currencyCode = await db.Accounts.AsNoTracking()
            .Where(a => accounts.Contains(a.Id))
            .Select(a => a.CurrencyCode)
            .SingleAsync(cancellationToken);

        // Read the clock before building the entity: its CreatedAt default is taken at construction, and the
        // writer rejects an OccurredAt that is later than CreatedAt.
        var occurredAt = clock.GetUtcNow();
        var entity = new Transaction
        {
            Id = id,
            AccountId = accountId,
            Kind = transaction.Kind,
            Direction = direction,
            Amount = transaction.Amount,
            CurrencyCode = currencyCode,
            RawDescription = transaction.Description,
            NormalizedMerchant = transaction.Merchant,
            OccurredAt = occurredAt
        };

        // Insert and balance change in one DB transaction; a balance outside its range throws BalanceLimitException
        // and nothing is written. There is deliberately no balance check before this: another request could change
        // the balance in between, so only the atomic update decides. The same key again finds its own row: the same
        // event is a replay, a different one an IdempotencyConflictException.
        var outcome = await TransactionWriter.AddAsync(db, entity, cancellationToken);

        // A replay reads the stored row, so it returns the first answer (its original time included).
        return new TransactionWriteResult(await reader.GetAsync(customerId, id, cancellationToken),
            Replayed: outcome == WriteOutcome.AlreadyExisted);
    }
}
