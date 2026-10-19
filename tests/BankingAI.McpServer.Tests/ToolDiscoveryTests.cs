using System.Text.Json;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.McpServer.Tests;

// MCP-02: what the model is shown. The tool set is an allowlist checked here, so a tool cannot be published without a
// test (the MCP counterpart of the API's endpoint sweep); descriptions are the AI-14a ones word for word (K3-A).
public sealed class ToolDiscoveryTests
{
    private const string PeriodHelp =
        "period: Today=bugün, Yesterday=dün, ThisWeek=bu hafta (pazartesiden bugüne), LastWeek=geçen hafta, " +
        "Last7Days=son 7 gün, Last30Days=son 30 gün, ThisMonth=bu ay, LastMonth=geçen ay, Last3Months=son 3 ay, " +
        "Last6Months=son 6 ay, ThisYear=bu yıl.";

    private static readonly Dictionary<string, (string Description, string[] Arguments)> Published = new()
    {
        ["get_spending_summary"] = ("Bir dönemdeki toplam harcamayı para birimi başına döndürür. " + PeriodHelp, ["period"]),
        ["get_category_breakdown"] = ("Bir dönemdeki harcamanın kategorilere dağılımını döndürür. " + PeriodHelp, ["period"]),
        ["get_merchant_spending"] = (
            "Bir dönemde tek bir işyerine (merchant) yapılan harcamayı döndürür. merchant: kullanıcının söylediği işyeri adı. " + PeriodHelp,
            ["merchant", "period"]),
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
    public async Task No_tool_takes_a_customer_an_account_or_dates_and_every_argument_is_required()
    {
        var tools = await ToolsAsync();

        Assert.All(tools, t =>
        {
            var properties = t.JsonSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal);
            Assert.Equal(Published[t.Name].Arguments, properties);
            Assert.Equal(Published[t.Name].Arguments,
                t.JsonSchema.GetProperty("required").EnumerateArray().Select(r => r.GetString()!).Order(StringComparer.Ordinal));
        });
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_period_lists_exactly_the_eleven_names_and_never_a_custom_range()
    {
        var tools = await ToolsAsync();

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

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Every_tool_declares_the_shape_of_its_result()
    {
        var tools = await ToolsAsync();

        Assert.All(tools, t => Assert.Equal(JsonValueKind.Object, t.ReturnJsonSchema?.ValueKind));
    }
}
