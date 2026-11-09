using System.ComponentModel;
using BankingAI.Application.Analytics;
using BankingAI.Hosting.Security;
using ModelContextProtocol.Server;

namespace BankingAI.McpServer.Tools;

/// <summary>
/// The two periods a complete-month analysis accepts (INC-03 K2-C). "This month" is not offered, so the model cannot choose
/// it; a calendar month the user named comes as <see cref="Custom"/> with the first and last day the AI service read.
/// </summary>
public enum ToolCompleteMonth
{
    LastMonth,
    Custom
}

/// <summary>
/// Spending against income (INC-03): the share of the income base a complete month took (INC-01) and the rule-based saving
/// advice of that month (INC-02), from the same Application services as the API. Services come through the constructor for
/// the reason given on <see cref="SpendingTools"/>.
/// </summary>
[McpServerToolType]
public sealed class IncomeTools(
    IHttpContextAccessor httpContext, IncomeShareService incomeShares, SpendingAdviceService advice, ILogger<IncomeTools> logger)
{
    private const string MonthHelp = "period: LastMonth=geçen ay.";

    /// <summary><c>get_income_share</c>: a complete month's spending, and each category, as a share of the income base.</summary>
    [McpServerTool(Name = "get_income_share", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Tamamlanmış bir ayın harcamasının maaşa (gelire) oranını ve kategorilerin gelir payını döndürür; \"maaşımın ne kadarını "
        + "harcadım\" soruları için. " + MonthHelp)]
    public Task<IncomeShare> GetIncomeShareAsync(
        ToolCompleteMonth period, string? from = null, string? to = null, CancellationToken cancellationToken = default) =>
        ToolErrors.RunAsync(() => incomeShares.GetAsync(
            new IncomeShareQuery(CustomerId, ToPeriodKind(period), ToolDate.Read(from, nameof(from)), ToolDate.Read(to, nameof(to))),
            cancellationToken), logger);

    /// <summary>
    /// <c>get_spending_advice</c>: essential and discretionary spending of a complete month and at most two pieces of advice,
    /// only when a rule holds.
    /// </summary>
    [McpServerTool(Name = "get_spending_advice", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Tamamlanmış bir ayın harcamasını temel ve isteğe bağlı diye ayırır, yalnız kurallar tutarsa en fazla 2 tasarruf önerisi "
        + "verir; \"nerede tasarruf edebilirim\" soruları için. " + MonthHelp)]
    public Task<SpendingAdvice> GetSpendingAdviceAsync(
        ToolCompleteMonth period, string? from = null, string? to = null, CancellationToken cancellationToken = default) =>
        ToolErrors.RunAsync(() => advice.GetAsync(
            new SpendingAdviceQuery(CustomerId, ToPeriodKind(period), ToolDate.Read(from, nameof(from)), ToolDate.Read(to, nameof(to))),
            cancellationToken), logger);

    // The customer comes from the token only (K2-A).
    private Guid CustomerId => httpContext.HttpContext!.User.GetCustomerId();

    private static PeriodKind ToPeriodKind(ToolCompleteMonth period) => Enum.Parse<PeriodKind>(period.ToString());
}
