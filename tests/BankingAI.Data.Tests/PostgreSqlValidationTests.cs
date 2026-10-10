using BankingAI.Data.Entities;
using BankingAI.Data.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace BankingAI.Data.Tests;

[Collection(PostgreSqlCollection.Name)]
public class PostgreSqlValidationTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Validation_and_database_constraints_reject_invalid_records_and_rollback()
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        await using var db = new BankingDbContext();
        var before = await Counts(db);
        await using var scope = await db.Database.BeginTransactionAsync();
        var customer = new Customer { FirstName = "Validation", LastName = "Test" };
        var account = new Account { CustomerId = customer.Id, AccountName = "Validation", AccountNumber = Guid.NewGuid().ToString(), CurrencyCode = "TRY" };
        var category = await db.TransactionCategories.FirstOrDefaultAsync(c => c.Code == "FOOD");
        if (category is null)
        {
            var used = await db.TransactionCategories.Select(c => c.Id).ToListAsync();
            category = new TransactionCategory { Id = FreeId(used), Code = "FOOD", DisplayName = "Food" };
            db.Add(category);
        }
        var asset = new MarketAsset
        {
            Id = FreeId(await db.MarketAssets.Select(a => a.Id).ToListAsync()),
            Symbol = "TEST_" + Guid.NewGuid().ToString("N")[..20], Name = "Validation",
            AssetType = "Currency", BaseUnit = "USD", QuoteCurrency = "TRY", IsActive = true
        };
        db.AddRange(customer, account, asset);
        await db.SaveChangesAsync();
        Assert.Equal(customer.CreatedAt, account.CreatedAt);
        var purchase = new Transaction
        {
            AccountId = account.Id, Amount = 12.3400m, Direction = TransactionDirection.Debit,
            Kind = TransactionKind.Purchase, CurrencyCode = "TRY", RawDescription = "Validation", OccurredAt = DateTimeOffset.UtcNow.AddYears(-10)
        };
        db.Add(purchase);
        db.SaveChanges(); // Both sync and async overrides must work.
        var price = new DailyMarketPrice { MarketAssetId = asset.Id, PriceDate = new(2020, 1, 2), Price = 1.23456789m };
        db.Add(price);
        await db.SaveChangesAsync();

        async Task Check(string sql, string constraint)
        {
            await scope.CreateSavepointAsync("invalid_input");
            try
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
                Assert.Equal("23514", error.SqlState);
                Assert.Equal(constraint, error.ConstraintName);
            }
            finally { await scope.RollbackToSavepointAsync("invalid_input"); }
        }
        var c = $"UPDATE \"Customers\" SET {{0}} WHERE \"Id\" = '{customer.Id}'";
        var a = $"UPDATE \"Accounts\" SET {{0}} WHERE \"Id\" = '{account.Id}'";
        var t = $"UPDATE \"Transactions\" SET {{0}} WHERE \"Id\" = '{purchase.Id}'";
        var p = $"UPDATE \"DailyMarketPrices\" SET {{0}} WHERE \"Id\" = {price.Id}";
        await Check(string.Format(c, "\"Id\" = '00000000-0000-0000-0000-000000000000'"), "CK_Customers_Id_NotEmpty");
        await Check(string.Format(c, "\"FirstName\" = E'\\t\\n'"), "CK_Customers_FirstName_NotBlank");
        await Check(string.Format(c, "\"LastName\" = E'\\t\\n'"), "CK_Customers_LastName_NotBlank");
        await Check(string.Format(c, "\"CreatedAt\" = 'infinity'"), "CK_Customers_CreatedAt_Finite");
        await Check(string.Format(c, "\"CreatedAt\" = '0001-01-01T00:00:00Z'"), "CK_Customers_CreatedAt_Finite");
        await Check(string.Format(a, "\"Balance\" = -0.01"), "CK_Accounts_Balance_NonNegative");
        await Check(string.Format(a, "\"AccountType\" = 0"), "CK_Accounts_AccountType");
        await Check(string.Format(a, "\"AccountType\" = 3"), "CK_Accounts_AccountType");
        await Check(string.Format(a, "\"CreatedAt\" = '-infinity'"), "CK_Accounts_CreatedAt_Finite");
        await Check(string.Format(t, "\"NormalizedMerchant\" = E'\\t'"), "CK_Transactions_NormalizedMerchant_NotBlank");
        await Check(string.Format(t, "\"OccurredAt\" = 'infinity'"), "CK_Transactions_Timestamps");
        await Check(string.Format(t, "\"OccurredAt\" = '0001-01-01T00:00:00Z'"), "CK_Transactions_Timestamps");
        await Check(string.Format(t, "\"CreatedAt\" = 'infinity'"), "CK_Transactions_Timestamps");
        await Check(string.Format(t, $"\"CategoryId\" = {category.Id}, \"CategorizationSource\" = 1, \"CategorizedAt\" = \"CreatedAt\" - interval '1 second'"), "CK_Transactions_Timestamps");
        // KIND-01: each statement below violates exactly one Kind constraint.
        await Check(string.Format(t, "\"Kind\" = 0"), "CK_Transactions_Kind");
        await Check(string.Format(t, "\"Kind\" = 6"), "CK_Transactions_Kind");
        await Check(string.Format(t, "\"Kind\" = 2, \"PairId\" = gen_random_uuid()"), "CK_Transactions_Kind_Direction");
        await Check(string.Format(t, "\"Kind\" = 3, \"PairId\" = NULL"), "CK_Transactions_Kind_Pair");
        await Check(string.Format(t, "\"Direction\" = 2, \"Kind\" = 4, \"PairId\" = gen_random_uuid()"), "CK_Transactions_Kind_Pair");
        await Check(string.Format(t, "\"Kind\" = 3, \"PairId\" = '00000000-0000-0000-0000-000000000000'"), "CK_Transactions_PairId_NotEmpty");
        await Check(string.Format(t, $"\"Kind\" = 3, \"PairId\" = gen_random_uuid(), \"CategoryId\" = {category.Id}, \"CategorizationSource\" = 1, \"CategorizedAt\" = \"CreatedAt\""), "CK_Transactions_Kind_Category");
        await Check(string.Format(t, $"\"Direction\" = 2, \"Kind\" = 4, \"CategoryId\" = {category.Id}, \"CategorizationSource\" = 1, \"CategorizedAt\" = \"CreatedAt\""), "CK_Transactions_Kind_Category");

        // A pair accepts one Debit and one Credit leg; a second leg in the same direction is rejected.
        await scope.CreateSavepointAsync("pair_legs");
        var pairId = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync(string.Format(t, $"\"Kind\" = 3, \"PairId\" = '{pairId}'"));
        var insertLeg = $$"""
            INSERT INTO "Transactions" ("Id", "AccountId", "Amount", "Direction", "Kind", "PairId", "CurrencyCode", "RawDescription", "OccurredAt", "CreatedAt")
            VALUES (gen_random_uuid(), '{{account.Id}}', 12.34, {0}, 3, '{{pairId}}', 'TRY', 'Pair leg', now() - interval '1 day', now())
            """;
        await db.Database.ExecuteSqlRawAsync(string.Format(insertLeg, 2));
        var duplicateLeg = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(string.Format(insertLeg, 1)));
        Assert.Equal("23505", duplicateLeg.SqlState);
        Assert.Equal("IX_Transactions_PairId_Direction", duplicateLeg.ConstraintName);
        await scope.RollbackToSavepointAsync("pair_legs");
        await Check(string.Format(p, "\"Source\" = E'\\n'"), "CK_DailyMarketPrices_Source_NotBlank");
        foreach (var invalidDate in new[] { "0001-01-01", "9999-12-31", "infinity", "-infinity" })
            await Check(string.Format(p, $"\"PriceDate\" = '{invalidDate}'"), "CK_DailyMarketPrices_PriceDate_Finite");

        // Exhaust the nullable combinations: a SQL CHECK must not pass via UNKNOWN.
        foreach (var id in new[] { "NULL", category.Id.ToString() })
        foreach (var source in new[] { "NULL", "1", "2", "3" })
        foreach (var confidence in new[] { "NULL", "0.85" })
        foreach (var at in new[] { "NULL", "\"CreatedAt\"" })
        {
            var valid = id == "NULL" ? source == "NULL" && confidence == "NULL" && at == "NULL" :
                at != "NULL" && (source == "2" ? confidence != "NULL" : (source is "1" or "3") && confidence == "NULL");
            var sql = string.Format(t, $"\"CategoryId\" = {id}, \"CategorizationSource\" = {source}, \"CategoryConfidence\" = {confidence}, \"CategorizedAt\" = {at}");
            if (!valid) await Check(sql, "CK_Transactions_Categorization");
            else
            {
                await scope.CreateSavepointAsync("valid_input");
                await db.Database.ExecuteSqlRawAsync(sql);
                await scope.RollbackToSavepointAsync("valid_input");
            }
        }

        // Validation must also catch values assigned through EF, bypassing the init guard.
        db.Entry(purchase).Property(x => x.Amount).CurrentValue = 12.345m;
        Assert.Throws<ArgumentException>(() => db.SaveChanges());
        await Assert.ThrowsAsync<ArgumentException>(() => db.SaveChangesAsync());
        Assert.Equal(12.34m, await db.Transactions.AsNoTracking().Where(x => x.Id == purchase.Id).Select(x => x.Amount).SingleAsync());
        db.Entry(purchase).Property(x => x.Amount).CurrentValue = 12.34m;
        db.Entry(account).Property(x => x.Balance).CurrentValue = -0.01m;
        await Assert.ThrowsAsync<ArgumentException>(() => db.SaveChangesAsync());
        db.Entry(account).Property(x => x.Balance).CurrentValue = 0m;
        db.Entry(customer).Property(x => x.CreatedAt).CurrentValue = DateTimeOffset.UtcNow.AddDays(-1);
        await Assert.ThrowsAsync<ArgumentException>(() => db.SaveChangesAsync());
        db.Entry(customer).Property(x => x.CreatedAt).CurrentValue = db.Entry(customer).Property(x => x.CreatedAt).OriginalValue;
        purchase.SetCategorization(category.Id, CategorizationSource.Llm, 0.85000m);
        await db.SaveChangesAsync();
        purchase.SetCategorization(category.Id, CategorizationSource.Manual);
        await db.SaveChangesAsync();
        Assert.Null(purchase.CategoryConfidence);
        purchase.ClearCategorization();
        await db.SaveChangesAsync();
        var persisted = await db.Transactions.AsNoTracking().SingleAsync(x => x.Id == purchase.Id);
        Assert.Null(persisted.CategoryId); Assert.Null(persisted.CategorizationSource);
        Assert.Null(persisted.CategoryConfidence); Assert.Null(persisted.CategorizedAt);
        Assert.Equal(price.PriceDate, await db.DailyMarketPrices.Where(x => x.Id == price.Id).Select(x => x.PriceDate).SingleAsync());

        await scope.RollbackAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(before, await Counts(db));
    }

    private static short FreeId(List<short> used) => (short)Enumerable.Range(1, short.MaxValue).First(i => !used.Contains((short)i));

    private static async Task<int[]> Counts(BankingDbContext db) =>
        [await db.Customers.CountAsync(), await db.Accounts.CountAsync(), await db.Transactions.CountAsync(),
         await db.TransactionCategories.CountAsync(), await db.MarketAssets.CountAsync(), await db.DailyMarketPrices.CountAsync()];
}
