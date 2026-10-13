using BankingAI.Data.Entities;
using BankingAI.Data.Enums;
using BankingAI.Data.Validation;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Data.Writing;

/// <summary>
/// Writes one unpaired transaction and the matching account balance change in a single DB transaction.
/// Used by the seeds and by the API; the balance is changed atomically inside PostgreSQL.
/// </summary>
public static class TransactionWriter
{
    private const decimal MaximumBalance = 9999999999999999.99m;

    public static async Task<WriteOutcome> AddAsync(
        BankingDbContext db,
        Transaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(transaction);

        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("TransactionWriter kendi DB transaction'ını yönetir; mevcut transaction ile çağrılamaz.");
        if (db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("TransactionWriter çağrısından önce DbContext'teki değişiklikleri kaydedin veya temizleyin.");

        // Writing one leg of a pair here would leave half a transfer/sale; KIND-03 adds the two-leg writer.
        if (transaction.PairId is not null)
            throw new InvalidOperationException("PairId taşıyan işlem tek bacaklı yazıcıyla yazılamaz; iki bacaklı yazıcı kullanılmalı.");

        var referenceUtc = DateTimeOffset.UtcNow;
        EntityValidation.Validate(transaction, referenceUtc);
        if (transaction.Direction is not (TransactionDirection.Debit or TransactionDirection.Credit))
            throw new ArgumentOutOfRangeException(nameof(transaction), "Direction Debit veya Credit olmalı.");

        await using var dbTransaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // The same id again is a retry (API-12): it must never apply the balance delta a second time.
            var existing = await db.Transactions.AsNoTracking().SingleOrDefaultAsync(t => t.Id == transaction.Id, cancellationToken);
            if (existing is not null)
                return Replay(existing, transaction); // nothing was written; disposing rolls the transaction back

            db.Transactions.Add(transaction);
            await db.SaveChangesAsync(cancellationToken);

            await ApplyBalanceDeltaAsync(db, transaction, cancellationToken);

            await dbTransaction.CommitAsync(cancellationToken);
            return WriteOutcome.Created;
        }
        catch (DbUpdateException error) when (IsUniqueViolation(error))
        {
            // A concurrent retry inserted the same id between our check and our insert. Its row is committed
            // (the unique index waited for it), so it is compared like any other existing row.
            await dbTransaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            var existing = await db.Transactions.AsNoTracking().SingleOrDefaultAsync(t => t.Id == transaction.Id, cancellationToken);
            if (existing is null)
                throw;
            return Replay(existing, transaction);
        }
        catch
        {
            // Rollback must still be attempted if the caller token was cancelled.
            await dbTransaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    // A stored row with the same identity: the same event is a harmless retry, anything else a conflict.
    private static WriteOutcome Replay(Transaction stored, Transaction incoming) =>
        SameEvent(stored, incoming) ? WriteOutcome.AlreadyExisted : throw new IdempotencyConflictException();

    /// <summary>
    /// Whether two transactions describe the same event: everything the caller states. The server's own fields
    /// (id, OccurredAt, CreatedAt, categorization) are left out; a retry arrives later and gets a new time.
    /// </summary>
    internal static bool SameEvent(Transaction stored, Transaction incoming) =>
        stored.AccountId == incoming.AccountId && stored.Kind == incoming.Kind && stored.Direction == incoming.Direction
        && stored.Amount == incoming.Amount && stored.CurrencyCode == incoming.CurrencyCode
        && stored.RawDescription == incoming.RawDescription && stored.NormalizedMerchant == incoming.NormalizedMerchant
        && stored.PairId == incoming.PairId;

    // PostgreSQL 23505: a primary key or unique index refused the row.
    internal static bool IsUniqueViolation(DbUpdateException error) =>
        error.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation };

    // Shared by the single- and two-leg writers. PostgreSQL evaluates the arithmetic against the
    // locked current row, so concurrent writes add their deltas instead of overwriting a read balance.
    internal static async Task ApplyBalanceDeltaAsync(
        BankingDbContext db,
        Transaction transaction,
        CancellationToken cancellationToken)
    {
        var delta = transaction.Direction == TransactionDirection.Credit
            ? transaction.Amount
            : -transaction.Amount;
        var updated = await db.Accounts
            .Where(a => a.Id == transaction.AccountId && a.CurrencyCode == transaction.CurrencyCode)
            .Where(a => a.Balance + delta >= 0m && a.Balance + delta <= MaximumBalance)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.Balance, a => a.Balance + delta), cancellationToken);

        if (updated != 1)
        {
            var accountExists = await db.Accounts.AsNoTracking()
                .AnyAsync(a => a.Id == transaction.AccountId && a.CurrencyCode == transaction.CurrencyCode, cancellationToken);
            if (!accountExists)
                throw new InvalidOperationException("Hesap bulunamadı veya işlem para birimi hesapla eşleşmiyor.");

            // A distinct type, so callers can tell this state-dependent refusal from a programming error.
            throw new BalanceLimitException(transaction.Direction == TransactionDirection.Debit
                ? "Yetersiz bakiye nedeniyle Debit işlemi reddedildi."
                : "Credit işlemi hesap bakiyesinin üst sınırını aşar.");
        }

        // ExecuteUpdate bypasses EF's change tracker. Refresh a previously loaded Account
        // so this context does not keep exposing its old balance.
        var trackedAccount = db.ChangeTracker.Entries<Account>()
            .SingleOrDefault(entry => entry.Entity.Id == transaction.AccountId);
        if (trackedAccount is not null)
        {
            var currentBalance = await db.Accounts.AsNoTracking()
                .Where(a => a.Id == transaction.AccountId)
                .Select(a => a.Balance)
                .SingleAsync(cancellationToken);
            var balance = trackedAccount.Property(a => a.Balance);
            balance.CurrentValue = currentBalance;
            balance.OriginalValue = currentBalance;
            balance.IsModified = false;
        }
    }
}
