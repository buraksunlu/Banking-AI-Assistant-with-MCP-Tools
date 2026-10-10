using BankingAI.Data.Entities;
using BankingAI.Data.Enums;
using BankingAI.Data.Validation;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Data.Seeding;

/// <summary>
/// Seed amaçlı transaction insert'i ile account bakiyesini tek DB transaction'ında yazar.
/// </summary>
public static class TransactionSeedWriter
{
    private const decimal MaximumBalance = 9999999999999999.99m;

    public static async Task AddAsync(
        BankingDbContext db,
        Transaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(transaction);

        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("TransactionSeedWriter kendi DB transaction'ını yönetir; mevcut transaction ile çağrılamaz.");
        if (db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("TransactionSeedWriter çağrısından önce DbContext'teki değişiklikleri kaydedin veya temizleyin.");

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
            // A duplicate stable Transaction ID must never apply the balance delta again.
            if (await db.Transactions.AsNoTracking().AnyAsync(t => t.Id == transaction.Id, cancellationToken))
                throw new InvalidOperationException($"Transaction Id {transaction.Id} zaten mevcut; bakiye ikinci kez değiştirilmedi.");

            db.Transactions.Add(transaction);
            await db.SaveChangesAsync(cancellationToken);

            await ApplyBalanceDeltaAsync(db, transaction, cancellationToken);

            await dbTransaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // Rollback must still be attempted if the caller token was cancelled.
            await dbTransaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

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

            throw new InvalidOperationException(transaction.Direction == TransactionDirection.Debit
                ? "Yetersiz bakiye nedeniyle Debit işlemi reddedildi."
                : "Credit işlemi hesap bakiye sınırını aşar.");
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
