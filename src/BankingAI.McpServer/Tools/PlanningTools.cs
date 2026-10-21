using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using BankingAI.Application.Analytics;
using BankingAI.Hosting.Security;
using ModelContextProtocol.Server;

namespace BankingAI.McpServer.Tools;

/// <summary>
/// The planning tools (MCP-17, MCP-18): a budget the user states and a cut the user imagines, both worked out by the
/// backend's own services (ANA-13, ANA-14). These are the first tools whose arguments are the user's own figures; the AI
/// service checks that the user said them (decisions K3-A, K4-A) and this server checks their shape. Services come
/// through the constructor for the reason given on <see cref="SpendingTools"/>.
/// </summary>
[McpServerToolType]
public sealed class PlanningTools(
    IHttpContextAccessor httpContext, BudgetCheckService budgets, SavingsSimulationService savings, ILogger<PlanningTools> logger)
{
    // The category codes are the schema's enum only. Listing their Turkish meanings in the descriptions (the first draft)
    // cost the older tools their periods in the tool-selection eval ("geçen hafta" became Last7Days): the 4b model reads
    // every description on every turn, so these two are kept short. "Yalnız … söylediyse" kept "Geçen hafta Starbucks'a" on LastWeek.

    /// <summary>
    /// <c>check_budget</c>: this month's spending against the budget the user gives (never stored): spent, remaining or
    /// over, daily average and the linear month-end projection.
    /// </summary>
    [McpServerTool(Name = "check_budget", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Yalnız kullanıcı bir bütçe tutarı söylediyse: bu ayın harcamasını o bütçeyle karşılaştırır (kalan, ay sonu tahmini). "
        + "amount: bütçe tutarı. currencyCode: TRY=TL/lira, EUR=euro, USD=dolar, GBP=sterlin. categoryCode ya da merchant: bütçe bir "
        + "kategori ya da işyeri içinse.")]
    public Task<BudgetCheck> CheckBudgetAsync(
        decimal amount, ToolCurrency currencyCode, ToolCategory? categoryCode = null,
        [MinLength(1), MaxLength(150)] string? merchant = null, CancellationToken cancellationToken = default) =>
        ToolErrors.RunAsync(() => budgets.CheckAsync(
            new BudgetCheckQuery(CustomerId, amount, currencyCode.ToString(), categoryCode?.ToString(), merchant), cancellationToken), logger);

    /// <summary>
    /// <c>simulate_savings</c>: the past monthly average of the last <paramref name="months"/> complete months times the
    /// reduction, per month and per year. Not investment advice: no return, interest or inflation.
    /// </summary>
    [McpServerTool(Name = "simulate_savings", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Bir harcamayı yüzde kaç azaltırsa ayda ve yılda tahminen ne kadar biriktireceğini hesaplar; yatırım getirisi değildir. "
        + "reductionPercent: azaltma oranı, 1-100 (yarıya = 50). months: geriye kaç tam ay, 1-12; verilmezse 6. categoryCode ya da "
        + "merchant: azaltılacak harcama bir kategori ya da işyeriyse.")]
    public Task<SavingsSimulation> SimulateSavingsAsync(
        [Range(1, SavingsSimulationService.MaximumReductionPercent)] int reductionPercent, ToolCategory? categoryCode = null,
        [MinLength(1), MaxLength(150)] string? merchant = null,
        [Range(1, 12)] int months = SavingsSimulationService.DefaultMonths, CancellationToken cancellationToken = default) =>
        ToolErrors.RunAsync(() => savings.SimulateAsync(
            new SavingsSimulationQuery(CustomerId, reductionPercent, categoryCode?.ToString(), merchant, months), cancellationToken), logger);

    // The customer comes from the token only (K2-A).
    private Guid CustomerId => httpContext.HttpContext!.User.GetCustomerId();
}
