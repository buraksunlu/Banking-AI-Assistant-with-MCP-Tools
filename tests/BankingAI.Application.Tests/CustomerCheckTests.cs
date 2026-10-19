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
