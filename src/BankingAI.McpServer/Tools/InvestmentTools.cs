using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using BankingAI.Application.Analytics;
using BankingAI.Application.Investment;
using BankingAI.Hosting.Security;
using ModelContextProtocol.Server;

namespace BankingAI.McpServer.Tools;

/// <summary>The only currency of a monthly saving (INV-01 K15-B): every asset is quoted in lira.</summary>
public enum ToolLiraCurrency
{
    TRY
}

/// <summary>
/// The historical "what if" scenarios (INC-03, todo 12f): past prices only, no forecast, no product, no order. Every result
/// carries the backend's fixed warning; the AI service shows it under every answer (K6-A). Services come through the
/// constructor for the reason given on <see cref="SpendingTools"/>.
/// </summary>
[McpServerToolType]
public sealed class InvestmentTools(IHttpContextAccessor httpContext, WhatIfService whatIf, ILogger<InvestmentTools> logger)
{
    /// <summary><c>what_if_one_off</c>: a period's lira spending bought on its first day as USD, EUR and gram gold, valued today.</summary>
    [McpServerTool(Name = "what_if_one_off", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Bir dönemin TL harcaması dönemin ilk günü dolar, euro ve gram altın olarak alınsaydı bugün geçmiş fiyatla ne ederdi; "
        + "\"harcamasaydım / dolar alsaydım\" soruları için, yatırım tavsiyesi değildir. categoryCode: yalnız bir kategori içinse. "
        + SpendingTools.PeriodHelp)]
    public Task<OneOffWhatIf> WhatIfOneOffAsync(
        ToolPeriod period, ToolCategory? categoryCode = null, string? from = null, string? to = null,
        CancellationToken cancellationToken = default) =>
        ToolErrors.RunAsync(() => whatIf.OneOffAsync(
            new OneOffWhatIfQuery(CustomerId, Enum.Parse<PeriodKind>(period.ToString()), ToolDate.Read(from, nameof(from)),
                ToolDate.Read(to, nameof(to)), categoryCode?.ToString()),
            cancellationToken), logger);

    /// <summary>
    /// <c>what_if_monthly</c>: one lira amount bought on the first day of each of the last <paramref name="months"/> complete months,
    /// valued today, with each asset's volatility. The amount is the user's (with its currency) or INC-02's advice, never both. The
    /// months are required (INV-03 K3-A): the chat asks the user rather than letting a twelve be guessed.
    /// </summary>
    [McpServerTool(Name = "what_if_monthly", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Aynı TL tutarı son birkaç ayın her ayı dolar, euro ve gram altın olarak biriktirseydi bugün ne ederdi ve fiyat ne kadar "
        + "dalgalandı; yatırım tavsiyesi değildir. months: kaç ay biriktirildiği, 2-22. amount: kullanıcının söylediği aylık tutar, "
        + "currencyCode: TRY. fromAdvice: tutar yerine önerilen aylık tasarruf isteniyorsa true.")]
    public Task<MonthlyWhatIf> WhatIfMonthlyAsync(
        [Range(WhatIfMath.MinimumMonths, WhatIfMath.MaximumMonths)] int months, decimal? amount = null,
        ToolLiraCurrency? currencyCode = null, bool fromAdvice = false, CancellationToken cancellationToken = default) =>
        ToolErrors.RunAsync(() => whatIf.MonthlyAsync(
            new MonthlyWhatIfQuery(CustomerId, amount, currencyCode?.ToString(), fromAdvice, months), cancellationToken), logger);

    // The customer comes from the token only (K2-A).
    private Guid CustomerId => httpContext.HttpContext!.User.GetCustomerId();
}
