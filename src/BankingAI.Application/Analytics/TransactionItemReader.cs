using System.Globalization;
using BankingAI.Data;
using BankingAI.Data.Entities;
using BankingAI.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Analytics;

internal enum TransactionOrder
{
    NewestFirst = 1,
    LargestFirst = 2
}

// Turns a scoped transaction query into TransactionItem DTOs with one SQL query. Account name,
// category and the pair's other leg come from joins; masking and local dates are applied here
// once, so every service that lists transactions returns the same shape.
internal static class TransactionItemReader
{
    public static async Task<List<TransactionItem>> ReadAsync(
        BankingDbContext db, IQueryable<Transaction> source, TransactionOrder order, int skip, int take,
        Guid customerId, string timeZoneId, CancellationToken cancellationToken)
    {
        var joined =
            from t in source
            join a in db.Accounts on t.AccountId equals a.Id
            join c in db.TransactionCategories on t.CategoryId equals (short?)c.Id into categories
            from c in categories.DefaultIfEmpty()
            // EF keeps C# null semantics ("null == null" is true) and, when it turns this into a
            // LEFT JOIN, drops a null check on the outer row t. The guard therefore sits on the
            // joined row o; otherwise every unpaired row joins every other unpaired row.
            from other in db.Transactions
                .Where(o => o.PairId != null && o.PairId == t.PairId && o.Id != t.Id)
                .DefaultIfEmpty()
            from otherAccount in db.Accounts.Where(oa => oa.Id == other!.AccountId).DefaultIfEmpty()
            from otherOwner in db.Customers.Where(oc => oc.Id == otherAccount!.CustomerId).DefaultIfEmpty()
            select new
            {
                t.Id, t.AccountId, a.AccountName, t.OccurredAt, t.Kind, t.Direction, t.Amount, t.CurrencyCode,
                t.NormalizedMerchant, CategoryCode = (string?)c!.Code, CategoryName = (string?)c!.DisplayName,
                t.RawDescription,
                OtherAccountName = (string?)otherAccount!.AccountName,
                OtherAccountType = (AccountType?)otherAccount!.AccountType,
                OtherCustomerId = (Guid?)otherAccount!.CustomerId,
                OtherFirstName = (string?)otherOwner!.FirstName,
                OtherLastName = (string?)otherOwner!.LastName
            };

        // Ordering is applied to the final joined query: an ORDER BY placed before the joins is not
        // guaranteed to survive them in SQL. Ties always fall back to newest first, then Id.
        var ordered = order switch
        {
            TransactionOrder.NewestFirst => joined.OrderByDescending(r => r.OccurredAt).ThenByDescending(r => r.Id),
            TransactionOrder.LargestFirst => joined.OrderByDescending(r => r.Amount)
                .ThenByDescending(r => r.OccurredAt).ThenByDescending(r => r.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(order))
        };

        var rows = await ordered.Skip(skip).Take(take).ToListAsync(cancellationToken);

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        return rows.Select(r =>
        {
            var ownAccount = r.OtherCustomerId == customerId;
            string? counterparty = r.OtherCustomerId is null ? null
                : ownAccount || r.OtherAccountType == AccountType.Business ? r.OtherAccountName
                : MaskName(r.OtherFirstName!, r.OtherLastName!);
            return new TransactionItem(
                r.Id, r.AccountId, r.AccountName, r.OccurredAt,
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.OccurredAt, timeZone).DateTime),
                r.Kind, r.Direction, r.Amount, r.CurrencyCode, r.NormalizedMerchant, r.CategoryCode,
                r.Kind == TransactionKind.Purchase ? r.CategoryName ?? AnalyticsLabels.Unclassified : null,
                r.RawDescription, counterparty, ownAccount);
        }).ToList();
    }

    // "Deniz M***": first name in clear, first letter of the surname, never the full surname.
    private static string MaskName(string firstName, string lastName) =>
        $"{firstName} {StringInfo.GetNextTextElement(lastName.Trim())}***";
}
