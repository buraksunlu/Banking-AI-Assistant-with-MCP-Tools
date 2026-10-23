using BankingAI.Application.Analytics;
using BankingAI.Application.Transactions;
using BankingAI.Data;
using BankingAI.Data.Enums;
using Xunit;

namespace BankingAI.Application.Tests;

// API-17. The whole history ends with the business day, like every other period: with the demo clock fixed in the past,
// a transaction written later (by the API on the real clock) is not part of it. The demo data has nothing after the demo
// moment, so this needs its own rows: a TemporaryCustomer, deleted afterwards.
public sealed class AllTimeBoundaryTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds());

    private static async Task WriteAsync(Guid customerId, Guid accountId, DateTimeOffset at, decimal amount)
    {
        await using var db = new BankingDbContext();
        var clock = new FixedTimeProvider(at);
        var reader = new TransactionSearchService(db, new PeriodResolver(clock));
        await new TransactionWriteService(db, new PeriodResolver(clock), clock, reader).CreateAsync(customerId, accountId,
            new NewTransaction(TransactionKind.Salary, amount, "API-17 test", null), Guid.NewGuid().ToString());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task All_time_stops_at_the_end_of_the_business_day()
    {
        await using var customer = await TemporaryCustomer.CreateAsync();
        var account = await customer.AddAccountAsync("TRY");
        await WriteAsync(customer.CustomerId, account, Now.AddDays(-3), 100m);
        await WriteAsync(customer.CustomerId, account, Now, 200m);

        await using var db = new BankingDbContext();
        var businessDay = Now.AddDays(-2);
        var result = await new TransactionSearchService(db, new PeriodResolver(new FixedTimeProvider(businessDay)))
            .SearchAsync(new(customer.CustomerId, PeriodKind.AllTime));

        var item = Assert.Single(result.Items);
        Assert.Equal((100m, 1), (item.Amount, result.TotalCount));
        Assert.Equal(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(businessDay,
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")).DateTime), result.Period.EndDate);
    }
}
