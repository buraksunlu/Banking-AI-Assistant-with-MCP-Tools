using System.Text.Json;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.McpServer.Tests;

// MCP-05, MCP-07, MCP-25 and MCP-08/09/10/16/24/27: the same figures as the API, in the API's own JSON (K9-A). Expected values are the
// hand-computed fixtures of docs/synthetic-seed.md at the demo moment (10 October 2026), the same ones the API tests use.
public sealed class ToolCallTests
{
    private static async Task<JsonElement> CallAsync(Guid customerId, string tool, string arguments)
    {
        await using var server = McpTestServer.Create();
        await using var client = await McpTestServer.ConnectAsync(server, customerId);
        return McpTestServer.Result(await McpTestServer.CallAsync(client, tool, arguments));
    }

    // Raw JSON text: money must stay a number with its two decimals, exactly as the API writes it.
    private static string Money(JsonElement value) => value.GetRawText();

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("ThisMonth", "735.00", 5, "145.00", 1)] // the 1.500 TL transfer is not spending
    [InlineData("Yesterday", "145.00", 1, "145.00", 1)]
    [InlineData("ThisWeek", "615.00", 4, "145.00", 1)]
    public async Task Spending_summary_has_the_apis_figures(string period, string total, int count, string unclassified, int unclassifiedCount)
    {
        var result = await CallAsync(Ada, "get_spending_summary", $$"""{"period":"{{period}}"}""");

        Assert.Equal(period, result.GetProperty("period").GetProperty("kind").GetString());
        var tl = Assert.Single(result.GetProperty("currencies").EnumerateArray());
        Assert.Equal(("TRY", total, count, unclassified, unclassifiedCount),
            (tl.GetProperty("currencyCode").GetString(), Money(tl.GetProperty("totalAmount")), tl.GetProperty("transactionCount").GetInt32(),
             Money(tl.GetProperty("uncategorizedAmount")), tl.GetProperty("uncategorizedCount").GetInt32()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Spending_summary_of_this_month_covers_1_to_10_october()
    {
        var period = (await CallAsync(Ada, "get_spending_summary", """{"period":"ThisMonth"}""")).GetProperty("period");

        Assert.Equal(("2026-10-01", "2026-10-10"), (period.GetProperty("startDate").GetString(), period.GetProperty("endDate").GetString()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Category_breakdown_keeps_the_unclassified_group_apart()
    {
        var result = await CallAsync(Ada, "get_category_breakdown", """{"period":"ThisMonth"}""");

        var tl = Assert.Single(result.GetProperty("currencies").EnumerateArray());
        Assert.Equal(
            [("FOOD", "590.00", "80.27"), (null, "145.00", "19.73")],
            tl.GetProperty("categories").EnumerateArray()
                .Select(c => (c.GetProperty("categoryCode").GetString(), Money(c.GetProperty("amount")), Money(c.GetProperty("percentage")))));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("Starbucks", false)]
    [InlineData("starbuck", true)] // a single containing name is accepted, and the result says so
    public async Task Merchant_spending_matches_the_customers_own_merchant(string merchant, bool partial)
    {
        var result = await CallAsync(Ada, "get_merchant_spending", $$"""{"merchant":"{{merchant}}","period":"Last3Months"}""");

        var match = result.GetProperty("match");
        Assert.Equal(("Matched", "Starbucks", partial),
            (match.GetProperty("status").GetString(), match.GetProperty("merchant").GetString(), match.GetProperty("isPartialMatch").GetBoolean()));
        var tl = Assert.Single(result.GetProperty("currencies").EnumerateArray());
        Assert.Equal(("TRY", "930.00", 5),
            (tl.GetProperty("currencyCode").GetString(), Money(tl.GetProperty("totalAmount")), tl.GetProperty("transactionCount").GetInt32()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Merchant_spending_never_guesses_between_merchants()
    {
        var ambiguous = await CallAsync(Ege, "get_merchant_spending", """{"merchant":"şehir","period":"Last6Months"}""");
        var foreign = await CallAsync(Ada, "get_merchant_spending", """{"merchant":"Aile Marketi","period":"ThisMonth"}""");

        Assert.Equal("Ambiguous", ambiguous.GetProperty("match").GetProperty("status").GetString());
        Assert.Equal(["Şehir Ulaşım", "Şehirlerarası Otobüs"],
            ambiguous.GetProperty("match").GetProperty("candidates").EnumerateArray().Select(c => c.GetString()));
        Assert.Empty(ambiguous.GetProperty("currencies").EnumerateArray());
        // Deniz's market is not one of Ada's merchants.
        Assert.Equal("NotFound", foreign.GetProperty("match").GetProperty("status").GetString());
        Assert.Empty(foreign.GetProperty("currencies").EnumerateArray());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Each_customer_sees_only_their_own_spending()
    {
        // Same tool, same period, another token: Deniz's own last month (one purchase of 31.000 TL).
        var result = await CallAsync(Deniz, "get_spending_summary", """{"period":"LastMonth"}""");

        var tl = Assert.Single(result.GetProperty("currencies").EnumerateArray());
        Assert.Equal(("31000.00", 1), (Money(tl.GetProperty("totalAmount")), tl.GetProperty("transactionCount").GetInt32()));
    }

    // --- MCP-08, MCP-09, MCP-10, MCP-16, MCP-24, MCP-27 ---

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Merchant_breakdown_lists_this_months_merchants_largest_first()
    {
        var result = await CallAsync(Ada, "get_merchant_breakdown", """{"period":"ThisMonth"}""");

        var tl = Assert.Single(result.GetProperty("currencies").EnumerateArray());
        Assert.Equal("735.00", Money(tl.GetProperty("totalAmount")));
        // 375/735 = 51,02 %, 215/735 = 29,25 %, 145/735 = 19,73 %; the purchase without a merchant keeps its own row.
        Assert.Equal(
            [("Starbucks", "375.00", 2, "51.02"), ("Kahve Durağı", "215.00", 2, "29.25"), ("Sınıflandırılmamış", "145.00", 1, "19.73")],
            tl.GetProperty("merchants").EnumerateArray().Select(m => (m.GetProperty("displayName").GetString(), Money(m.GetProperty("amount")),
                m.GetProperty("transactionCount").GetInt32(), Money(m.GetProperty("percentage")))));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Highest_transactions_are_last_months_purchases_largest_first()
    {
        var result = await CallAsync(Ada, "get_highest_transactions", """{"period":"LastMonth"}""");

        // September has only four purchases, so the limit of five returns all of them; the transfers are not spending.
        var tl = Assert.Single(result.GetProperty("currencies").EnumerateArray());
        Assert.Equal(
            [("2026-09-12", "Netflix", "229.99"), ("2026-09-20", "Starbucks", "185.00"), ("2026-09-15", "Kahve Durağı", "110.00"),
             ("2026-09-18", "Spotify", "99.99")],
            tl.GetProperty("items").EnumerateArray().Select(i => (i.GetProperty("localDate").GetString(), i.GetProperty("merchant").GetString(),
                Money(i.GetProperty("amount")))));
        Assert.All(tl.GetProperty("items").EnumerateArray(), i => Assert.Equal("Purchase", i.GetProperty("kind").GetString()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Previous_full_month_compares_september_with_august()
    {
        var result = await CallAsync(Ada, "compare_spending", """{"comparison":"PreviousFullMonth"}""");

        Assert.Equal(("2026-09-01", "2026-09-30", "2026-08-01", "2026-08-31"),
            (result.GetProperty("current").GetProperty("startDate").GetString(), result.GetProperty("current").GetProperty("endDate").GetString(),
             result.GetProperty("previous").GetProperty("startDate").GetString(), result.GetProperty("previous").GetProperty("endDate").GetString()));
        var tl = Assert.Single(result.GetProperty("currencies").EnumerateArray());
        // 110 / 514,98 = 21,36 %; the food category caused it, the subscriptions did not change.
        Assert.Equal(("624.98", 4, "514.98", 3, "110.00", "21.36"),
            (Money(tl.GetProperty("currentAmount")), tl.GetProperty("currentCount").GetInt32(), Money(tl.GetProperty("previousAmount")),
             tl.GetProperty("previousCount").GetInt32(), Money(tl.GetProperty("change")), Money(tl.GetProperty("percentageChange"))));
        Assert.Equal([("FOOD", "110.00"), ("SUBSCRIPTION", "0.00")],
            tl.GetProperty("categories").EnumerateArray().Select(c => (c.GetProperty("categoryCode").GetString(), Money(c.GetProperty("change")))));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Month_to_date_against_an_empty_start_of_september_has_no_percentage()
    {
        var result = await CallAsync(Ada, "compare_spending", """{"comparison":"MonthToDate"}""");

        // 1-10 September has no purchase (the first one is on the 12th): a growth rate from zero is never invented. The empty
        // side is written 0.0 (the SQL sum's scale), so it is compared as a number.
        var tl = Assert.Single(result.GetProperty("currencies").EnumerateArray());
        Assert.Equal(("735.00", 0m, JsonValueKind.Null),
            (Money(tl.GetProperty("currentAmount")), tl.GetProperty("previousAmount").GetDecimal(), tl.GetProperty("percentageChange").ValueKind));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Monthly_trend_of_three_months_adds_the_running_month()
    {
        var result = await CallAsync(Ada, "get_monthly_trend", """{"months":3}""");

        var tl = Assert.Single(result.GetProperty("currencies").EnumerateArray());
        Assert.Equal(
            [(7, false, "514.98"), (8, false, "514.98"), (9, false, "624.98"), (10, true, "735.00")],
            tl.GetProperty("months").EnumerateArray().Select(m => (m.GetProperty("month").GetInt32(), m.GetProperty("isPartial").GetBoolean(),
                Money(m.GetProperty("amount")))));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Recurring_payments_are_netflix_and_spotify()
    {
        var result = await CallAsync(Ada, "get_recurring_payments", "{}");

        Assert.Equal([("Netflix", "229.99", 6, "2026-10-12"), ("Spotify", "99.99", 6, "2026-10-18")],
            result.GetProperty("payments").EnumerateArray().Select(p => (p.GetProperty("merchant").GetString(),
                Money(p.GetProperty("pattern").GetProperty("latestAmount")), p.GetProperty("pattern").GetProperty("occurrenceCount").GetInt32(),
                p.GetProperty("pattern").GetProperty("nextExpectedDate").GetString())));
        var total = Assert.Single(result.GetProperty("monthlyTotals").EnumerateArray());
        Assert.Equal(("TRY", "329.98"), (total.GetProperty("currencyCode").GetString(), Money(total.GetProperty("monthlyTotal"))));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("{}", "2026-11-09", new[] { "Netflix", "Spotify" }, "329.98")] // 30 days when the model leaves it out
    [InlineData("""{"days":5}""", "2026-10-15", new[] { "Netflix" }, "229.99")]
    public async Task Upcoming_payments_look_ahead_the_given_days(string arguments, string to, string[] merchants, string total)
    {
        var result = await CallAsync(Ada, "get_upcoming_payments", arguments);

        Assert.Equal(("2026-10-10", to), (result.GetProperty("from").GetString(), result.GetProperty("to").GetString()));
        Assert.Equal(merchants, result.GetProperty("payments").EnumerateArray().Select(p => p.GetProperty("merchant").GetString()));
        Assert.Equal(total, Money(Assert.Single(result.GetProperty("totals").EnumerateArray()).GetProperty("estimatedTotal")));
        var netflix = result.GetProperty("payments")[0];
        Assert.Equal(("2026-10-12", "229.99", 2, false),
            (netflix.GetProperty("estimatedDate").GetString(), Money(netflix.GetProperty("estimatedAmount")), netflix.GetProperty("daysUntil").GetInt32(),
             netflix.GetProperty("isOverdue").GetBoolean()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Recurring_payments_come_from_the_tokens_customer_only()
    {
        // Deniz's monthly market grows from 12.000 to 31.000, so it is not a subscription; Ada's Netflix must not show up.
        var recurring = await CallAsync(Deniz, "get_recurring_payments", "{}");
        var upcoming = await CallAsync(Deniz, "get_upcoming_payments", "{}");

        Assert.Empty(recurring.GetProperty("payments").EnumerateArray());
        Assert.Empty(upcoming.GetProperty("payments").EnumerateArray());
    }
}
