using BankingAI.Data.Entities;
using BankingAI.Data.Enums;
using BankingAI.Data.Validation;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Data.Writing;

/// <summary>
/// Writes both legs of a transfer or an internal shop purchase, and both balance changes, in a single DB transaction.
/// </summary>
public static class PairedTransactionWriter
{
    public static async Task<WriteOutcome> AddAsync(
        BankingDbContext db,
        Transaction debit,
        Transaction credit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(debit);
        ArgumentNullException.ThrowIfNull(credit);

        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("PairedTransactionWriter kendi DB transaction'ını yönetir; mevcut transaction ile çağrılamaz.");
        if (db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("PairedTransactionWriter çağrısından önce DbContext'teki değişiklikleri kaydedin veya temizleyin.");

        // Every consistency rule is checked before the database is touched.
        ValidatePair(debit, credit);
        var referenceUtc = DateTimeOffset.UtcNow;
        EntityValidation.Validate(debit, referenceUtc);
        EntityValidation.Validate(credit, referenceUtc);
        var pairId = debit.PairId!.Value;

        await using var dbTransaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // A retried pair must not move money twice (KIND-03 decision, API-12): the same pair returns the first
            // result, a different one is a conflict. Concurrent duplicates that both pass this check are stopped by
            // UNIQUE (PairId, Direction) below and compared after the rollback.
            var existing = await LegsAsync(db, pairId, cancellationToken);
            if (existing.Count > 0)
                return Replay(existing, debit, credit); // nothing was written; disposing rolls the transaction back

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
                await TransactionWriter.ApplyBalanceDeltaAsync(db, leg, cancellationToken);

            db.Transactions.AddRange(debit, credit);
            await db.SaveChangesAsync(cancellationToken);

            await dbTransaction.CommitAsync(cancellationToken);
            return WriteOutcome.Created;
        }
        catch (DbUpdateException error) when (TransactionWriter.IsUniqueViolation(error))
        {
            // The balance updates of this attempt are undone with the rollback; the committed pair is compared.
            await dbTransaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            var existing = await LegsAsync(db, pairId, cancellationToken);
            if (existing.Count == 0)
                throw;
            return Replay(existing, debit, credit);
        }
        catch
        {
            // Rollback must still be attempted if the caller token was cancelled.
            await dbTransaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private static Task<List<Transaction>> LegsAsync(BankingDbContext db, Guid pairId, CancellationToken cancellationToken) =>
        db.Transactions.AsNoTracking().Where(t => t.PairId == pairId).ToListAsync(cancellationToken);

    // Both stored legs must be the same events as the incoming ones; leg ids may differ (a retry builds new objects).
    private static WriteOutcome Replay(List<Transaction> stored, Transaction debit, Transaction credit)
    {
        var storedDebit = stored.SingleOrDefault(t => t.Direction == TransactionDirection.Debit);
        var storedCredit = stored.SingleOrDefault(t => t.Direction == TransactionDirection.Credit);
        return storedDebit is not null && storedCredit is not null && stored.Count == 2
               && TransactionWriter.SameEvent(storedDebit, debit) && TransactionWriter.SameEvent(storedCredit, credit)
            ? WriteOutcome.AlreadyExisted
            : throw new IdempotencyConflictException();
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
