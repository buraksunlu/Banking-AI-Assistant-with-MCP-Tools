using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using BankingAI.Application.Analytics;
using BankingAI.Hosting.Security;
using ModelContextProtocol.Server;

namespace BankingAI.McpServer.Tools;

/// <summary>
/// The recurring payment tools (MCP-27, MCP-16). Which payments are monthly is decided only by the backend's recurring
/// rule (last six months, at least three payments); upcoming dates and amounts are that rule's estimates, never promises.
/// Services come through the constructor for the reason given on <see cref="SpendingTools"/>.
/// </summary>
[McpServerToolType]
public sealed class PaymentTools(
    IHttpContextAccessor httpContext, RecurringPaymentService recurring, UpcomingPaymentService upcoming, ILogger<PaymentTools> logger)
{
    /// <summary>
    /// <c>get_recurring_payments</c>: the monthly payments of the last six months and their monthly total per currency.
    /// No argument: the six months are the rule itself, as in the API.
    /// </summary>
    [McpServerTool(Name = "get_recurring_payments", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Son 6 ayda her ay düzenli tekrarlanan ödemeleri (abonelikler) ve aylık toplamlarını döndürür. Argüman almaz.")]
    public Task<RecurringPayments> GetRecurringPaymentsAsync(CancellationToken cancellationToken) =>
        ToolErrors.RunAsync(() => recurring.GetAsync(new RecurringPaymentQuery(CustomerId), cancellationToken), logger);

    /// <summary>
    /// <c>get_upcoming_payments</c>: the recurring payments expected in the next <paramref name="days"/> days (the API's
    /// 30 when the model leaves it out), with estimated dates and amounts.
    /// </summary>
    [McpServerTool(Name = "get_upcoming_payments", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Düzenli ödemelere göre önümüzdeki günlerde beklenen ödemeleri tahmini tarih ve tutarla döndürür. days: kaç gün ileriye "
        + "bakılacağı, 1-31; verilmezse 30.")]
    public Task<UpcomingPayments> GetUpcomingPaymentsAsync(
        [Range(1, UpcomingPaymentService.MaximumDays)] int days = UpcomingPaymentService.DefaultDays, CancellationToken cancellationToken = default) =>
        ToolErrors.RunAsync(() => upcoming.GetAsync(new UpcomingPaymentQuery(CustomerId, days), cancellationToken), logger);

    // The customer comes from the token only (K2-A).
    private Guid CustomerId => httpContext.HttpContext!.User.GetCustomerId();
}
