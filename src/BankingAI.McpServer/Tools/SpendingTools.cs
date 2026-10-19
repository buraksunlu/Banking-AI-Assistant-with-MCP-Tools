using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using BankingAI.Application.Analytics;
using BankingAI.Hosting.Security;
using ModelContextProtocol.Server;

namespace BankingAI.McpServer.Tools;

/// <summary>
/// The spending tools (MCP-05, MCP-07, MCP-25). Each one reads the customer from the token, calls the same Application
/// service as the API (decision K6-A) and returns the same result shape (K9-A). Names and descriptions are the ones the
/// model was measured with in AI-14a (12 of 12), moved word for word (K3-A).
/// </summary>
/// <remarks>
/// Services come through the constructor, never as method parameters: the SDK guesses which method parameters are
/// services, and a wrong guess (seen intermittently in tests) publishes the service as an argument the model must fill.
/// The SDK creates the instance per call from the request's services, so the scoped services are the request's own.
/// </remarks>
[McpServerToolType]
public sealed class SpendingTools(
    IHttpContextAccessor httpContext, SpendingSummaryService summaries, CategoryBreakdownService categories,
    MerchantSpendingService merchants, ILogger<SpendingTools> logger)
{
    /// <summary>The period meanings, added to every description: this is how the model picked the right one.</summary>
    public const string PeriodHelp =
        "period: Today=bugün, Yesterday=dün, ThisWeek=bu hafta (pazartesiden bugüne), LastWeek=geçen hafta, " +
        "Last7Days=son 7 gün, Last30Days=son 30 gün, ThisMonth=bu ay, LastMonth=geçen ay, Last3Months=son 3 ay, " +
        "Last6Months=son 6 ay, ThisYear=bu yıl.";

    /// <summary><c>get_spending_summary</c>: total spending per currency, with the unclassified part.</summary>
    [McpServerTool(Name = "get_spending_summary", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Bir dönemdeki toplam harcamayı para birimi başına döndürür. " + PeriodHelp)]
    public Task<SpendingSummary> GetSpendingSummaryAsync(ToolPeriod period, CancellationToken cancellationToken) =>
        ToolErrors.RunAsync(() => summaries.GetAsync(Spending(period), cancellationToken), logger);

    /// <summary><c>get_category_breakdown</c>: spending per category and currency, largest first.</summary>
    [McpServerTool(Name = "get_category_breakdown", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Bir dönemdeki harcamanın kategorilere dağılımını döndürür. " + PeriodHelp)]
    public Task<CategoryBreakdown> GetCategoryBreakdownAsync(ToolPeriod period, CancellationToken cancellationToken) =>
        ToolErrors.RunAsync(() => categories.GetAsync(Spending(period), cancellationToken), logger);

    /// <summary>
    /// <c>get_merchant_spending</c>: spending at one merchant. The text is matched against the customer's own merchants;
    /// an ambiguous or unknown name returns the match without amounts, never a guess.
    /// </summary>
    [McpServerTool(Name = "get_merchant_spending", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Bir dönemde tek bir işyerine (merchant) yapılan harcamayı döndürür. merchant: kullanıcının söylediği işyeri adı. " + PeriodHelp)]
    public Task<MerchantSpendingResult> GetMerchantSpendingAsync(
        // 150 is the API's limit for a merchant name (Transactions.NormalizedMerchant); blank text is the service's rule.
        [MinLength(1), MaxLength(150)] string merchant, ToolPeriod period, CancellationToken cancellationToken) =>
        ToolErrors.RunAsync(() => merchants.GetSpendingAsync(Spending(period), merchant, cancellationToken), logger);

    // The customer comes from the token only (K2-A); no tool has a customer, account or date argument. The names are
    // the same in both enums, so the conversion is by name.
    private SpendingQuery Spending(ToolPeriod period) =>
        new(httpContext.HttpContext!.User.GetCustomerId(), Enum.Parse<PeriodKind>(period.ToString()), From: null, To: null, AccountId: null);
}
