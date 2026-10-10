using BankingAI.Data.Entities;
using BankingAI.Data.Enums;
using BankingAI.Data.Validation;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Data.Seeding;

/// <summary>
/// Transfer veya iç dükkân alışverişinin iki bacağını ve iki bakiyeyi tek DB transaction'ında yazar.
/// </summary>
public static class PairedTransactionSeedWriter
{
    public static async Task AddAsync(
        BankingDbContext db,
        Transaction debit,
        Transaction credit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(debit);
        ArgumentNullException.ThrowIfNull(credit);

        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("PairedTransactionSeedWriter kendi DB transaction'ını yönetir; mevcut transaction ile çağrılamaz.");
        if (db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("PairedTransactionSeedWriter çağrısından önce DbContext'teki değişiklikleri kaydedin veya temizleyin.");

        // Every consistency rule is checked before the database is touched.
        ValidatePair(debit, credit);
        var referenceUtc = DateTimeOffset.UtcNow;
        EntityValidation.Validate(debit, referenceUtc);
        EntityValidation.Validate(credit, referenceUtc);
        var pairId = debit.PairId!.Value;

        await using var dbTransaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // A retried pair must not move money twice. Concurrent duplicates that both pass this
            // check are still stopped by UNIQUE (PairId, Direction) and rolled back.
            if (await db.Transactions.AsNoTracking().AnyAsync(t => t.PairId == pairId, cancellationToken))
                throw new InvalidOperationException($"PairId {pairId} zaten mevcut; bakiyeler ikinci kez değiştirilmedi.");

            // Sales income belongs to a shop. A missing account is reported by the balance update below.
            if (credit.Kind == TransactionKind.Sale)
            {
                var creditAccountType = await db.Accounts.AsNoTracking()
                    .Where(a => a.Id == credit.AccountId)
                    .Select(a => (AccountType?)a.AccountType)
                    .SingleOrDefaultAsync(cancellationToken);
                if (creditAccountType is { } type && type != AccountType.Business)
                    throw new InvalidOperationException("Satış geliri (Sale) yalnızca Business hesaba yazılabilir.");
            }

            // Lock ordering: balances are always updated in ascending account id order, whatever the
            // direction. Opposite transfers (a→b, b→a) then wait on the same first row instead of
            // each holding one row and waiting for the other (deadlock, PostgreSQL 40P01).
            foreach (var leg in new[] { debit, credit }.OrderBy(t => t.AccountId))
                await TransactionSeedWriter.ApplyBalanceDeltaAsync(db, leg, cancellationToken);

            db.Transactions.AddRange(debit, credit);
            await db.SaveChangesAsync(cancellationToken);

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

    private static void ValidatePair(Transaction debit, Transaction credit)
    {
        InputRules.Require(debit.Direction == TransactionDirection.Debit && credit.Direction == TransactionDirection.Credit,
            "Direction", "İlk bacak Debit, ikinci bacak Credit olmalı.");
        InputRules.Require(debit.PairId is { } pairId && pairId != Guid.Empty && credit.PairId == pairId,
            "PairId", "İki bacak aynı, boş olmayan PairId taşımalı.");
        InputRules.Require((debit.Kind, credit.Kind) is (TransactionKind.Transfer, TransactionKind.Transfer)
            or (TransactionKind.Purchase, TransactionKind.Sale),
            "Kind", "Yalnızca Transfer→Transfer veya Purchase→Sale çifti yazılabilir.");
        InputRules.Require(debit.Id != credit.Id, "Id", "İki bacak farklı Transaction Id taşımalı.");
        InputRules.Require(debit.AccountId != credit.AccountId, "AccountId", "Aynı hesaba çift yazılamaz.");
        InputRules.Require(debit.Amount == credit.Amount, "Amount", "İki bacağın tutarı aynı olmalı.");
        InputRules.Require(debit.CurrencyCode == credit.CurrencyCode, "CurrencyCode", "İki bacağın para birimi aynı olmalı.");
        InputRules.Require(debit.OccurredAt == credit.OccurredAt, "OccurredAt", "İki bacağın işlem zamanı aynı olmalı.");
    }
}
