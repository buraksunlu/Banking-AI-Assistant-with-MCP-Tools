using System.Text.Json;
using BankingAI.McpServer.Tools;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.McpServer.Tests;

// MCP-02: what the model is shown. The tool set is an allowlist checked here, so a tool cannot be published without a
// test (the MCP counterpart of the API's endpoint sweep); descriptions are the AI-14a ones word for word (K3-A), and the
// MCP-08/09/10/16/24/27 and MCP-17/18 ones are the texts the tool-selection eval was run with.
public sealed class ToolDiscoveryTests
{
    private const string PeriodHelp =
        "period: Today=bugün, Yesterday=dün, ThisWeek=bu hafta (pazartesiden bugüne), LastWeek=geçen hafta, " +
        "Last7Days=son 7 gün, Last30Days=son 30 gün, ThisMonth=bu ay, LastMonth=geçen ay, Last3Months=son 3 ay, " +
        "Last6Months=son 6 ay, ThisYear=bu yıl.";

    // Arguments and the required ones, each in ordinal order.
    private static readonly Dictionary<string, (string Description, string[] Arguments, string[] Required)> Published = new()
    {
        ["get_spending_summary"] = ("Bir dönemdeki toplam harcamayı para birimi başına döndürür; \"ne harcadım / ne kadar harcadım\" gibi genel "
            + "harcama soruları için ilk tercih. " + PeriodHelp, ["period"], ["period"]),
        ["get_category_breakdown"] = ("Bir dönemdeki harcamanın kategorilere dağılımını döndürür. " + PeriodHelp, ["period"], ["period"]),
        ["get_merchant_spending"] = (
            "Bir dönemde tek bir işyerine (merchant) yapılan harcamayı döndürür. merchant: kullanıcının söylediği işyeri adı. " + PeriodHelp,
            ["merchant", "period"], ["merchant", "period"]),
        ["get_merchant_breakdown"] = (
            "Bir dönemdeki harcamanın işyerlerine (merchant) dağılımını en çoktan aza döndürür; \"en çok nereye harcadım\" soruları için. "
            + PeriodHelp, ["period"], ["period"]),
        ["get_highest_transactions"] = (
            "Bir dönemdeki en yüksek tutarlı 5 harcamayı döndürür; yalnız \"en büyük / en pahalı harcamam\" soruları için, toplam harcama için değil. " + PeriodHelp,
            ["period"], ["period"]),
        ["compare_spending"] = (
            "Harcamayı önceki dönemle karşılaştırır: iki dönemin toplamı, fark ve değişime yol açan kategoriler. comparison: "
            + "PreviousFullMonth=geçen ay ile ondan önceki ay (\"geçen aya göre\"), MonthToDate=bu ayın bugüne kadarki kısmı ile geçen "
            + "ayın aynı günleri.", ["comparison"], ["comparison"]),
        ["get_monthly_trend"] = (
            "Son birkaç ayın harcamasını takvim ayı olarak döndürür, devam eden ay ayrıca işaretlidir; \"aylara göre nasıl değişti\" "
            + "soruları için. months: geriye kaç tam ay, 1-12.", ["months"], ["months"]),
        ["get_recurring_payments"] = (
            "Son 6 ayda her ay düzenli tekrarlanan ödemeleri (abonelikler) ve aylık toplamlarını döndürür. Argüman almaz.", [], []),
        ["get_upcoming_payments"] = (
            "Düzenli ödemelere göre önümüzdeki günlerde beklenen ödemeleri tahmini tarih ve tutarla döndürür. days: kaç gün ileriye "
            + "bakılacağı, 1-31; verilmezse 30.", ["days"], []),
        ["check_budget"] = (
            "Yalnız kullanıcı bir bütçe tutarı söylediyse: bu ayın harcamasını o bütçeyle karşılaştırır (kalan, ay sonu tahmini). "
            + "amount: bütçe tutarı. currencyCode: TRY=TL/lira, EUR=euro, USD=dolar, GBP=sterlin. categoryCode ya da merchant: bütçe bir "
            + "kategori ya da işyeri içinse.",
            ["amount", "categoryCode", "currencyCode", "merchant"], ["amount", "currencyCode"]),
        ["simulate_savings"] = (
            "Bir harcamayı yüzde kaç azaltırsa ayda ve yılda tahminen ne kadar biriktireceğini hesaplar; yatırım getirisi değildir. "
            + "reductionPercent: azaltma oranı, 1-100 (yarıya = 50). months: geriye kaç tam ay, 1-12; verilmezse 6. categoryCode ya da "
            + "merchant: azaltılacak harcama bir kategori ya da işyeriyse.",
            ["categoryCode", "merchant", "months", "reductionPercent"], ["reductionPercent"]),
    };

    private static async Task<IList<ModelContextProtocol.Client.McpClientTool>> ToolsAsync()
    {
        await using var server = McpTestServer.Create();
        await using var client = await McpTestServer.ConnectAsync(server, Ada);
        return await client.ListToolsAsync();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Exactly_the_allowlisted_tools_are_published()
    {
        var tools = await ToolsAsync();

        Assert.Equal(Published.Keys.Order(StringComparer.Ordinal), tools.Select(t => t.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Descriptions_are_the_measured_ones_word_for_word()
    {
        var tools = await ToolsAsync();

        Assert.All(tools, t => Assert.Equal(Published[t.Name].Description, t.Description));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Every_tool_is_marked_read_only_and_not_destructive()
    {
        var tools = await ToolsAsync();

        Assert.All(tools, t => Assert.Equal((true, false),
            (t.ProtocolTool.Annotations?.ReadOnlyHint, t.ProtocolTool.Annotations?.DestructiveHint)));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task No_tool_takes_a_customer_an_account_or_dates_and_only_the_listed_arguments_are_required()
    {
        var tools = await ToolsAsync();

        Assert.All(tools, t =>
        {
            var properties = t.JsonSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal);
            Assert.Equal(Published[t.Name].Arguments, properties);
            var required = t.JsonSchema.TryGetProperty("required", out var names)
                ? names.EnumerateArray().Select(r => r.GetString()!).Order(StringComparer.Ordinal).ToArray()
                : [];
            Assert.Equal(Published[t.Name].Required, required);
        });
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_period_lists_exactly_the_eleven_names_and_never_a_custom_range()
    {
        var tools = (await ToolsAsync()).Where(t => t.JsonSchema.GetProperty("properties").TryGetProperty("period", out _)).ToList();

        Assert.Equal(5, tools.Count);
        Assert.All(tools, t =>
        {
            var period = t.JsonSchema.GetProperty("properties").GetProperty("period");
            Assert.Equal(
                ["Today", "Yesterday", "ThisWeek", "LastWeek", "Last7Days", "Last30Days",
                 "ThisMonth", "LastMonth", "Last3Months", "Last6Months", "ThisYear"],
                period.GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
        });
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_merchant_is_text_of_1_to_150_characters()
    {
        var merchant = (await ToolsAsync()).Single(t => t.Name == "get_merchant_spending")
            .JsonSchema.GetProperty("properties").GetProperty("merchant");

        Assert.Equal(("string", 1, 150),
            (merchant.GetProperty("type").GetString(), merchant.GetProperty("minLength").GetInt32(), merchant.GetProperty("maxLength").GetInt32()));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("get_monthly_trend", "months", 1, 12)]
    [InlineData("get_upcoming_payments", "days", 1, 31)]
    [InlineData("simulate_savings", "reductionPercent", 1, 100)]
    [InlineData("simulate_savings", "months", 1, 12)]
    public async Task Counts_are_whole_numbers_within_their_limits(string tool, string argument, int minimum, int maximum)
    {
        var rule = (await ToolsAsync()).Single(t => t.Name == tool).JsonSchema.GetProperty("properties").GetProperty(argument);

        Assert.Equal(("integer", minimum, maximum),
            (rule.GetProperty("type").GetString(), rule.GetProperty("minimum").GetInt32(), rule.GetProperty("maximum").GetInt32()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_budget_is_a_number_in_one_of_the_four_currencies()
    {
        var properties = (await ToolsAsync()).Single(t => t.Name == "check_budget").JsonSchema.GetProperty("properties");

        // The kuruş and upper limits are the backend's money rule (InputRules.Money), as for every amount.
        Assert.Equal("number", properties.GetProperty("amount").GetProperty("type").GetString());
        Assert.Equal(["TRY", "USD", "EUR", "GBP"],
            properties.GetProperty("currencyCode").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
        foreach (var currency in Enum.GetNames<ToolCurrency>())
            BankingAI.Data.Validation.InputRules.Currency(currency, "currencyCode");
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("check_budget")]
    [InlineData("simulate_savings")]
    public async Task The_categories_are_exactly_the_category_tables_codes(string tool)
    {
        var category = (await ToolsAsync()).Single(t => t.Name == tool).JsonSchema.GetProperty("properties").GetProperty("categoryCode");
        await using var db = new BankingAI.Data.BankingDbContext();
        var codes = await db.TransactionCategories.OrderBy(c => c.Id).Select(c => c.Code).ToListAsync();

        // Optional: the published set is the table's codes plus null (left out), never a code the table does not have.
        Assert.Equal([.. codes, null], category.GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_comparison_offers_two_names_and_never_custom_dates()
    {
        var comparison = (await ToolsAsync()).Single(t => t.Name == "compare_spending").JsonSchema.GetProperty("properties").GetProperty("comparison");

        Assert.Equal(["PreviousFullMonth", "MonthToDate"], comparison.GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Every_tool_declares_the_shape_of_its_result()
    {
        var tools = await ToolsAsync();

        Assert.All(tools, t => Assert.Equal(JsonValueKind.Object, t.ReturnJsonSchema?.ValueKind));
    }
}
