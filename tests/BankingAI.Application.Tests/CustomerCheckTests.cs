using BankingAI.Application.Analytics;
using BankingAI.Application.Customers;
using BankingAI.Data;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

// AI-14b K10-A. The assistant endpoints touch no customer data themselves, so this check is what turns a stale token
// into "sign in again" before the model is asked anything.
public sealed class CustomerCheckTests
{
    private static BankingDbContext Db()
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        return new BankingDbContext();
    }

    private static ICustomerCheck Check(BankingDbContext db) =>
        new CustomerCheck(db, new PeriodResolver(new FixedTimeProvider(DemoNowUtc)));

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [MemberData(nameof(DemoCustomers))]
    public async Task An_existing_customer_passes(Guid customerId)
    {
        await using var db = Db();

        await Check(db).EnsureExistsAsync(customerId);
    }

    // EmptyCustomer has no accounts: existence is about the customer, not about having accounts.
    public static TheoryData<Guid> DemoCustomers => new(Ada, Deniz, Ege, EmptyCustomer);

    // CHAT-08 K9-C: the check also tells the customer's own day, which the chat sends along as "today".
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_customers_day_and_time_zone_are_returned()
    {
        await using var db = Db();

        var day = await Check(db).EnsureExistsAsync(Ada);

        Assert.Equal((new DateOnly(2026, 10, 10), "Europe/Istanbul"), (day.Today, day.TimeZone));
    }

    // 21:30 UTC is 00:30 on the 11th in Istanbul and 23:30 on the 10th in Berlin.
    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("10000000-0000-0000-0000-000000000001", 11)]
    [InlineData("10000000-0000-0000-0000-000000000008", 10)]
    public async Task The_day_follows_the_customers_own_time_zone(string customerId, int dayOfOctober)
    {
        await using var db = Db();
        var late = new CustomerCheck(db, new PeriodResolver(new FixedTimeProvider(new DateTimeOffset(2026, 10, 10, 21, 30, 0, TimeSpan.Zero))));

        var day = await late.EnsureExistsAsync(Guid.Parse(customerId));

        Assert.Equal(new DateOnly(2026, 10, dayOfOctober), day.Today);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_missing_customer_is_a_stale_session()
    {
        await using var db = Db();

        await Assert.ThrowsAsync<CustomerNotFoundException>(() => Check(db).EnsureExistsAsync(Guid.NewGuid()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task An_empty_id_is_refused_as_input()
    {
        await using var db = Db();

        var error = await Assert.ThrowsAsync<ArgumentException>(() => Check(db).EnsureExistsAsync(Guid.Empty));
        Assert.Equal("customerId", error.ParamName);
    }
}
