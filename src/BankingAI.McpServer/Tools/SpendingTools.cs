using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using BankingAI.Application.Analytics;
using BankingAI.Hosting.Security;
using ModelContextProtocol.Server;

namespace BankingAI.McpServer.Tools;

/// <summary>
/// The spending tools (MCP-05, MCP-07, MCP-25; MCP-08, MCP-09, MCP-10, MCP-24). Each one reads the customer from the token,
/// calls the same Application service as the API (decision K6-A) and returns the same result shape (K9-A). The first three
/// descriptions are the ones the model was measured with in AI-14a (12 of 12), moved word for word (K3-A); the others are
/// the texts of the MCP-08 tool-selection eval.
/// </summary>
/// <remarks>
/// Services come through the constructor, never as method parameters: the SDK guesses which method parameters are
/// services, and a wrong guess (seen intermittently in tests) publishes the service as an argument the model must fill.
/// The SDK creates the instance per call from the request's services, so the scoped services are the request's own.
/// </remarks>
[McpServerToolType]
public sealed class SpendingTools(
    IHttpContextAccessor httpContext, SpendingSummaryService summaries, CategoryBreakdownService categories,
    MerchantSpendingService merchants, HighestTransactionsService highest, SpendingComparisonService comparisons,
    ILogger<SpendingTools> logger)
{
    /// <summary>The period meanings, added to every description: this is how the model picked the right one.</summary>
    public const string PeriodHelp =
        "period: Today=bugün, Yesterday=dün, ThisWeek=bu hafta (pazartesiden bugüne), LastWeek=geçen hafta, " +
        "Last7Days=son 7 gün, Last30Days=son 30 gün, ThisMonth=bu ay, LastMonth=geçen ay, Last3Months=son 3 ay, " +
        "Last6Months=son 6 ay, ThisYear=bu yıl.";

    /// <summary><c>get_spending_summary</c>: total spending per currency, with the unclassified part.</summary>
    [McpServerTool(Name = "get_spending_summary", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Bir dönemdeki toplam harcamayı para birimi başına döndürür; \"ne harcadım / ne kadar harcadım\" soruları için. " + PeriodHelp)]
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

    /// <summary><c>get_merchant_breakdown</c>: spending per merchant and currency, largest first (MCP-08).</summary>
    [McpServerTool(Name = "get_merchant_breakdown", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Bir dönemdeki harcamanın işyerlerine (merchant) dağılımını en çoktan aza döndürür; \"en çok nereye harcadım\" soruları için. " + PeriodHelp)]
    public Task<MerchantBreakdown> GetMerchantBreakdownAsync(ToolPeriod period, CancellationToken cancellationToken) =>
        ToolErrors.RunAsync(() => merchants.GetBreakdownAsync(Spending(period), cancellationToken), logger);

    /// <summary>
    /// <c>get_highest_transactions</c>: the five largest purchases per currency (MCP-10, decision K4-A). The limit, the
    /// other transaction kinds and the amount range stay the API's: fewer arguments for the model to get wrong.
    /// </summary>
    [McpServerTool(Name = "get_highest_transactions", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Bir dönemdeki en yüksek tutarlı 5 harcamayı döndürür; yalnız \"en büyük / en pahalı harcamam\" soruları için, toplam harcama için değil. " + PeriodHelp)]
    public Task<HighestTransactions> GetHighestTransactionsAsync(ToolPeriod period, CancellationToken cancellationToken) =>
        ToolErrors.RunAsync(() => highest.GetAsync(new HighestTransactionsQuery(CustomerId, ToPeriodKind(period)), cancellationToken), logger);

    /// <summary><c>compare_spending</c>: two periods, the change and the categories that caused it (MCP-09).</summary>
    [McpServerTool(Name = "compare_spending", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Harcamayı önceki dönemle karşılaştırır: iki dönemin toplamı, fark ve değişime yol açan kategoriler. comparison: "
        + "PreviousFullMonth=geçen ay ile ondan önceki ay (\"geçen aya göre\"), MonthToDate=bu ayın bugüne kadarki kısmı ile geçen "
        + "ayın aynı günleri.")]
    public Task<SpendingComparison> CompareSpendingAsync(ToolComparison comparison, CancellationToken cancellationToken) =>
        ToolErrors.RunAsync(() => comparisons.CompareAsync(
            new SpendingComparisonQuery(CustomerId, Enum.Parse<ComparisonKind>(comparison.ToString())), cancellationToken), logger);

    /// <summary>
    /// <c>get_monthly_trend</c>: spending per calendar month for the last <paramref name="months"/> complete months plus the
    /// running month, a month without spending being 0 (MCP-24). Capped at 12 rather than the API's 24 to keep the facts
    /// short (decision K3-A); no category or merchant filter yet.
    /// </summary>
    [McpServerTool(Name = "get_monthly_trend", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Son birkaç ayın harcamasını takvim ayı olarak döndürür, devam eden ay ayrıca işaretlidir; \"aylara göre nasıl değişti\" "
        + "soruları için. months: geriye kaç tam ay, 1-12.")]
    public Task<SpendingTrend> GetMonthlyTrendAsync([Range(1, 12)] int months, CancellationToken cancellationToken) =>
        ToolErrors.RunAsync(() => comparisons.GetMonthlyTrendAsync(new SpendingTrendQuery(CustomerId, months), cancellationToken), logger);

    // The customer comes from the token only (K2-A); no tool has a customer, account or date argument.
    private Guid CustomerId => httpContext.HttpContext!.User.GetCustomerId();

    private SpendingQuery Spending(ToolPeriod period) => new(CustomerId, ToPeriodKind(period), From: null, To: null, AccountId: null);

    // The names are the same in both enums, so the conversion is by name.
    private static PeriodKind ToPeriodKind(ToolPeriod period) => Enum.Parse<PeriodKind>(period.ToString());
}
