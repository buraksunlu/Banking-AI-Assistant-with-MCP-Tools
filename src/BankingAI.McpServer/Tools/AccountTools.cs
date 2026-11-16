using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using BankingAI.Application.Accounts;
using BankingAI.Application.Analytics;
using BankingAI.Data.Enums;
using BankingAI.Hosting.Security;
using ModelContextProtocol.Server;

namespace BankingAI.McpServer.Tools;

/// <summary>Which way a transfer went, as the user says it (ACC-01a); left out, both.</summary>
public enum ToolTransferDirection
{
    Sent,
    Received
}

/// <summary>
/// The everyday banking questions (ACC-01a, K1-A: four tools, each name one job): balances, a purchase search with an amount
/// limit, income and transfers. The last three are the API's transaction search narrowed to one kind of transaction and the
/// personal accounts (the scope of every chat answer, CHAT-07), so a list and its totals always come from one filter (K2-A).
/// Services come through the constructor for the reason given on <see cref="SpendingTools"/>.
/// </summary>
[McpServerToolType]
public sealed class AccountTools(
    IHttpContextAccessor httpContext, IAccountReadService accounts, TransactionSearchService search, ILogger<AccountTools> logger)
{
    /// <summary>Rows a tool returns: enough for a card's first part and its "Tümünü göster"; the totals cover every row.</summary>
    public const int ListLimit = 10;

    /// <summary><c>get_accounts</c>: every account with its balance and the totals per account type and currency (K4-A).</summary>
    [McpServerTool(Name = "get_accounts", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Müşterinin hesaplarını ve güncel bakiyelerini döndürür; \"bakiyem / hesabımda ne kadar para var\" soruları için. Argüman almaz.")]
    public Task<AccountOverview> GetAccountsAsync(CancellationToken cancellationToken = default) =>
        ToolErrors.RunAsync(async () => AccountBalances.Summarize(await accounts.ListAsync(CustomerId, cancellationToken)), logger);

    /// <summary><c>search_transactions</c>: the purchases of a period, narrowed by an amount limit, a merchant or a category.</summary>
    [McpServerTool(Name = "search_transactions", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Bir dönemdeki harcamaları listeler ve toplar; tutar sınırıyla (\"2.000 TL üstü\"), bir işyeri ya da kategoriyle süzülebilir. "
        + "minAmount/maxAmount: kullanıcının söylediği tutar sınırı, currencyCode ile. merchant: işyeri adı. categoryCode: kategori. "
        + SpendingTools.PeriodHelp)]
    public Task<TransactionSearchResult> SearchTransactionsAsync(
        ToolPeriod period, string? from = null, string? to = null, decimal? minAmount = null, decimal? maxAmount = null,
        ToolCurrency? currencyCode = null, [MinLength(1), MaxLength(150)] string? merchant = null, ToolCategory? categoryCode = null,
        CancellationToken cancellationToken = default) =>
        ToolErrors.RunAsync(() => search.SearchAsync(
            Query(period, from, to, TransactionKind.Purchase) with
            {
                MinAmount = minAmount, MaxAmount = maxAmount, CurrencyCode = currencyCode?.ToString(), Merchant = merchant,
                CategoryCode = categoryCode?.ToString()
            }, cancellationToken), logger);

    /// <summary><c>get_income</c>: the salaries and other income credited in a period.</summary>
    [McpServerTool(Name = "get_income", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Bir dönemde hesaplara yatan maaş ve gelirleri listeler ve toplar; \"maaşım yattı mı / ne kadar gelirim oldu\" soruları için. "
        + SpendingTools.PeriodHelp)]
    public Task<TransactionSearchResult> GetIncomeAsync(
        ToolPeriod period, string? from = null, string? to = null, CancellationToken cancellationToken = default) =>
        ToolErrors.RunAsync(() => search.SearchAsync(Query(period, from, to, TransactionKind.Salary), cancellationToken), logger);

    /// <summary><c>get_transfers</c>: the transfers of a period, either way or one way, to or from one party (K3-A).</summary>
    [McpServerTool(Name = "get_transfers", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Bir dönemdeki para transferlerini listeler ve toplar; \"kime para gönderdim / kimden para geldi\" soruları için. "
        + "direction: Sent=gönderilen, Received=gelen; verilmezse ikisi. counterparty: kullanıcının söylediği kişi ya da hesap adı. "
        + SpendingTools.PeriodHelp)]
    public Task<TransactionSearchResult> GetTransfersAsync(
        ToolPeriod period, string? from = null, string? to = null, ToolTransferDirection? direction = null,
        [MinLength(1), MaxLength(150)] string? counterparty = null, CancellationToken cancellationToken = default) =>
        ToolErrors.RunAsync(() => search.SearchAsync(
            Query(period, from, to, TransactionKind.Transfer) with
            {
                Direction = direction switch
                {
                    ToolTransferDirection.Sent => TransactionDirection.Debit,
                    ToolTransferDirection.Received => TransactionDirection.Credit,
                    _ => null
                },
                Counterparty = counterparty
            }, cancellationToken), logger);

    // The customer comes from the token only (K2-A); personal accounts, as every chat answer counts.
    private TransactionSearchQuery Query(ToolPeriod period, string? from, string? to, TransactionKind kind) =>
        new(CustomerId, Enum.Parse<PeriodKind>(period.ToString()), ToolDate.Read(from, nameof(from)), ToolDate.Read(to, nameof(to)),
            Kind: kind, Limit: ListLimit, AccountType: AccountType.Personal);

    private Guid CustomerId => httpContext.HttpContext!.User.GetCustomerId();
}
