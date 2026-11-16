using BankingAI.Api.Assistant;
using BankingAI.Application.Analytics;
using BankingAI.Data.Enums;
using Xunit;

namespace BankingAI.Api.Tests;

// CHAT-07. The link under a chat answer is built from the checked tool call, never by the model. No database: the
// builder is a pure table from (tool, arguments) to a transaction-list query.
public sealed class AssistantLinksTests
{
    private static AssistantReply Reply(string tool, params (string Key, string Value)[] arguments) =>
        Reply(AssistantReplyType.Answer, tool, arguments);

    private static AssistantReply Reply(AssistantReplyType type, string tool, params (string Key, string Value)[] arguments) =>
        new(type, "cevap", [new AssistantToolCall(tool, arguments.ToDictionary(a => a.Key, a => a.Value))], [], [], [], "", [], []);

    private static TransactionLinkQuery Single(AssistantReply reply)
    {
        var link = Assert.Single(AssistantLinks.For(reply));
        Assert.Equal("İşlemleri gör", link.Title);
        return link.Query;
    }

    [Theory]
    [InlineData("get_spending_summary")]
    [InlineData("get_category_breakdown")]
    [InlineData("get_merchant_breakdown")]
    [InlineData("get_highest_transactions")]
    public void A_period_tool_links_to_the_purchases_of_personal_accounts_in_that_period(string tool)
    {
        // The figures of these tools count Personal-account purchases only, so the list asks for exactly that.
        var query = Single(Reply(tool, ("period", "ThisMonth")));

        Assert.Equal(new TransactionLinkQuery(PeriodKind.ThisMonth, null, null, TransactionKind.Purchase, AccountType.Personal, null, null), query);
    }

    [Fact]
    public void A_custom_period_keeps_its_dates()
    {
        // CHAT-08: "1-5 Eylül" runs as Custom with two calendar days; the list shows the same days.
        var query = Single(Reply("get_spending_summary", ("period", "Custom"), ("from", "2026-09-01"), ("to", "2026-09-05")));

        Assert.Equal((PeriodKind.Custom, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5)), (query.Period, query.From, query.To));
    }

    [Fact]
    public void A_merchant_tool_links_to_that_merchants_purchases()
    {
        var query = Single(Reply("get_merchant_spending", ("merchant", "Starbucks"), ("period", "LastMonth")));

        Assert.Equal(new TransactionLinkQuery(PeriodKind.LastMonth, null, null, TransactionKind.Purchase, AccountType.Personal, null, "Starbucks"), query);
    }

    [Theory]
    [InlineData("FOOD", null, "FOOD", null)]
    [InlineData(null, "Starbucks", null, "Starbucks")]
    [InlineData(null, null, null, null)]
    // The budget service looks at the merchant first and ignores the category then; the link does the same.
    [InlineData("FOOD", "Starbucks", null, "Starbucks")]
    public void A_budget_links_to_the_running_month_with_the_filter_it_measured(string? category, string? merchant, string? expectedCategory, string? expectedMerchant)
    {
        var arguments = new List<(string, string)> { ("amount", "1500"), ("currencyCode", "TRY") };
        if (category is not null) arguments.Add(("categoryCode", category));
        if (merchant is not null) arguments.Add(("merchant", merchant));

        var query = Single(Reply("check_budget", [.. arguments]));

        Assert.Equal(new TransactionLinkQuery(PeriodKind.ThisMonth, null, null, TransactionKind.Purchase, AccountType.Personal,
            expectedCategory, expectedMerchant), query);
    }

    // ACC-01b PR2 (K7-A): the three list tools of ACC-01a link to the rows behind their totals, with the same filters.

    [Fact]
    public void Income_links_to_the_salaries_of_the_period()
    {
        var query = Single(Reply("get_income", ("period", "LastMonth")));

        Assert.Equal(new TransactionLinkQuery(PeriodKind.LastMonth, null, null, TransactionKind.Salary, AccountType.Personal, null, null), query);
    }

    [Theory]
    [InlineData("Sent", TransactionDirection.Debit)]
    [InlineData("Received", TransactionDirection.Credit)]
    [InlineData(null, null)]
    public void Transfers_link_with_their_direction_and_person(string? direction, TransactionDirection? expected)
    {
        var arguments = new List<(string, string)> { ("period", "Last3Months"), ("counterparty", "Selin") };
        if (direction is not null) arguments.Add(("direction", direction));

        var query = Single(Reply("get_transfers", [.. arguments]));

        Assert.Equal(new TransactionLinkQuery(PeriodKind.Last3Months, null, null, TransactionKind.Transfer, AccountType.Personal, null, null,
            expected, "Selin"), query);
    }

    [Fact]
    public void A_search_links_with_its_amount_limit_merchant_and_category()
    {
        var query = Single(Reply("search_transactions", ("period", "LastMonth"), ("minAmount", "2000"), ("maxAmount", "5000.5"),
            ("currencyCode", "TRY"), ("merchant", "Migros Jet"), ("categoryCode", "GROCERY")));

        Assert.Equal(new TransactionLinkQuery(PeriodKind.LastMonth, null, null, TransactionKind.Purchase, AccountType.Personal, "GROCERY",
            "Migros Jet", MinAmount: 2000m, MaxAmount: 5000.5m, CurrencyCode: "TRY"), query);
    }

    [Theory]
    [InlineData("direction", "Both")]          // not a direction of the tool
    [InlineData("counterparty", "  ")]
    [InlineData("minAmount", "2.000")]         // the JSON way only: a Turkish thousands dot is not 2
    [InlineData("minAmount", "-5")]
    [InlineData("minAmount", "1e3")]
    [InlineData("currencyCode", "lira")]
    public void A_list_argument_that_cannot_be_read_gives_no_link(string key, string value)
    {
        var tool = key is "direction" or "counterparty" ? "get_transfers" : "search_transactions";
        var arguments = new List<(string, string)> { ("period", "LastMonth"), (key, value) };
        if (key == "minAmount") arguments.Add(("currencyCode", "TRY"));
        if (key == "currencyCode") arguments.Add(("minAmount", "100"));

        Assert.Empty(AssistantLinks.For(Reply(tool, [.. arguments])));
    }

    [Fact]
    public void An_amount_without_its_currency_gives_no_link()
    {
        // The search refuses an amount limit without a currency (currencies are never mixed); a wrong list is never shown.
        Assert.Empty(AssistantLinks.For(Reply("search_transactions", ("period", "LastMonth"), ("minAmount", "2000"))));
    }

    [Fact]
    public void The_balance_gets_no_link()
    {
        // Balances are not transactions: there is no list behind them.
        Assert.Empty(AssistantLinks.For(Reply("get_accounts")));
    }

    [Theory]
    [InlineData("compare_spending")]        // two periods: no single list
    [InlineData("get_monthly_trend")]
    [InlineData("get_recurring_payments")]  // estimates, not rows
    [InlineData("get_upcoming_payments")]
    [InlineData("simulate_savings")]
    [InlineData("a_tool_added_later")]
    public void Tools_without_a_matching_list_get_no_link(string tool)
    {
        Assert.Empty(AssistantLinks.For(Reply(tool, ("period", "ThisMonth"))));
    }

    [Theory]
    [InlineData("get_spending_summary", "Yesterdayy", null, null)]   // not a period name
    [InlineData("get_spending_summary", "Custom", "2026-09-01", null) ] // a range needs both days
    [InlineData("get_spending_summary", "Custom", null, "2026-09-05")]
    [InlineData("get_spending_summary", "Custom", "2026-9-1", "2026-09-05")]  // days are YYYY-MM-DD
    [InlineData("get_spending_summary", "Custom", "2026-02-30", "2026-03-05")]
    [InlineData("get_spending_summary", "ThisMonth", "2026-09-01", "2026-09-05")]  // a named period takes no days
    [InlineData("get_spending_summary", "AllTime", null, null)]      // not a tool period
    [InlineData("get_spending_summary", null, null, null)]
    public void An_argument_that_cannot_be_read_gives_no_link_and_does_not_fail_the_reply(string tool, string? period, string? from, string? to)
    {
        var arguments = new List<(string, string)>();
        if (period is not null) arguments.Add(("period", period));
        if (from is not null) arguments.Add(("from", from));
        if (to is not null) arguments.Add(("to", to));

        Assert.Empty(AssistantLinks.For(Reply(tool, [.. arguments])));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_merchant_tool_without_a_merchant_gets_no_link(string merchant)
    {
        Assert.Empty(AssistantLinks.For(Reply("get_merchant_spending", ("merchant", merchant), ("period", "ThisMonth"))));
        Assert.Empty(AssistantLinks.For(Reply("get_merchant_spending", ("period", "ThisMonth"))));
    }

    [Theory]
    [InlineData(AssistantReplyType.Clarification)]
    [InlineData(AssistantReplyType.OutOfScope)]
    public void Only_an_answer_carries_a_link(AssistantReplyType type)
    {
        Assert.Empty(AssistantLinks.For(Reply(type, "get_spending_summary", ("period", "ThisMonth"))));
    }

    [Fact]
    public void Two_calls_with_the_same_list_give_one_link()
    {
        var call = new AssistantToolCall("get_spending_summary", new Dictionary<string, string> { ["period"] = "ThisMonth" });
        var reply = new AssistantReply(AssistantReplyType.Answer, "cevap", [call, call], [], [], [], "", [], []);

        Assert.Single(AssistantLinks.For(reply));
    }
}
