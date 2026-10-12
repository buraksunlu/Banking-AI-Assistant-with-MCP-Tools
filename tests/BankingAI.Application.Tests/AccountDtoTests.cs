using BankingAI.Application.Accounts;
using BankingAI.Data;
using BankingAI.Data.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

// API-03. Expected values are the hand-computed fixtures of docs/synthetic-seed.md.
public sealed class AccountDtoTests
{
    private static BankingDbContext Db()
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        return new BankingDbContext();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Projection_maps_ada_accounts()
    {
        await using var db = Db();
        // Selected by id, not by owner: Data tests running in parallel open temporary accounts for Ada.
        Guid[] ids = [AdaTryAccount, AdaUsdAccount, AdaEmptyAccount];

        var accounts = await db.Accounts
            .Where(a => ids.Contains(a.Id))
            .OrderBy(a => a.AccountNumber)
            .Select(AccountDto.Projection)
            .ToListAsync();

        Assert.Equal(
            [
                (AdaEmptyAccount, "Ada boş TRY", "DUMMY-ADA-EMPTY-001", "TRY", AccountType.Personal, 0m),
                (AdaTryAccount, "Ada günlük TRY", "DUMMY-ADA-TRY-001", "TRY", AccountType.Personal, 577490.18m),
                (AdaUsdAccount, "Ada USD", "DUMMY-ADA-USD-001", "USD", AccountType.Personal, 500m)
            ],
            accounts.Select(a => (a.Id, a.AccountName, a.AccountNumber, a.CurrencyCode, a.AccountType, a.Balance)));
        Assert.All(accounts, a => Assert.NotEqual(default, a.CreatedAt));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Projection_keeps_the_business_type_of_the_shop_account()
    {
        await using var db = Db();

        var shop = await db.Accounts.Where(a => a.Id == DenizShopAccount).Select(AccountDto.Projection).SingleAsync();

        Assert.Equal(("Kahve Durağı – Kadıköy", "DUMMY-DENIZ-SHOP-001", AccountType.Business, 2595m),
            (shop.AccountName, shop.AccountNumber, shop.AccountType, shop.Balance));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public void Projection_never_reads_the_owner_column()
    {
        using var db = Db();

        var sql = db.Accounts.Select(AccountDto.Projection).ToQueryString();

        Assert.DoesNotContain("CustomerId", sql);
        Assert.Contains("\"Balance\"", sql);
    }
}
