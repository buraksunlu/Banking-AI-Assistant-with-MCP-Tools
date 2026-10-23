using System.Globalization;
using BankingAI.Application.Analytics;
using BankingAI.Data.Enums;

namespace BankingAI.Api.Assistant;

/// <summary>
/// The transaction-list query behind a chat answer's link: what <c>GET /api/transactions</c> is asked so the list
/// shows the rows the answer's figure was computed from. <c>From</c>/<c>To</c> are set only for <c>Custom</c>.
/// </summary>
public sealed record TransactionLinkQuery(
    PeriodKind Period, DateOnly? From, DateOnly? To, TransactionKind Kind, AccountType AccountType,
    string? CategoryCode, string? Merchant);

/// <summary>
/// A button under a chat answer (CHAT-07): the title to show and the list it opens.
/// </summary>
public sealed record AssistantLink(string Title, TransactionLinkQuery Query);

/// <summary>
/// Builds the links of a reply from its checked tool calls (CHAT-07). The backend decides, never the model: a table
/// from tool to list query. A call whose arguments cannot be read exactly gets no link, so a wrong list is never shown
/// next to a right figure; the reply itself is unaffected.
/// </summary>
public static class AssistantLinks
{
    private const string Title = "İşlemleri gör";

    // The tool periods of the MCP server; "Custom" carries two calendar days.
    private static readonly Dictionary<string, PeriodKind> Periods = new(StringComparer.Ordinal)
    {
        ["Today"] = PeriodKind.Today, ["Yesterday"] = PeriodKind.Yesterday, ["ThisWeek"] = PeriodKind.ThisWeek,
        ["LastWeek"] = PeriodKind.LastWeek, ["Last7Days"] = PeriodKind.Last7Days, ["Last30Days"] = PeriodKind.Last30Days,
        ["ThisMonth"] = PeriodKind.ThisMonth, ["LastMonth"] = PeriodKind.LastMonth, ["Last3Months"] = PeriodKind.Last3Months,
        ["Last6Months"] = PeriodKind.Last6Months, ["ThisYear"] = PeriodKind.ThisYear, ["Custom"] = PeriodKind.Custom
    };

    /// <summary>The links of an answer, without duplicates; empty for a clarification or an out-of-scope reply.</summary>
    public static IReadOnlyList<AssistantLink> For(AssistantReply reply)
    {
        if (reply.Type != AssistantReplyType.Answer)
            return [];
        return reply.ToolCalls
            .Select(call => QueryFor(call.Tool, call.Arguments))
            .OfType<TransactionLinkQuery>()
            .Distinct()
            .Select(query => new AssistantLink(Title, query))
            .ToList();
    }

    // Tools whose figure is a sum, a ranking or a check over purchases of Personal accounts get a list; comparisons,
    // series and estimates (recurring, upcoming payments, savings) have no single list of rows behind them.
    private static TransactionLinkQuery? QueryFor(string tool, IReadOnlyDictionary<string, string> arguments) => tool switch
    {
        "get_spending_summary" or "get_category_breakdown" or "get_merchant_breakdown" or "get_highest_transactions"
            => Period(arguments) is { } period ? Purchases(period, null, null) : null,
        "get_merchant_spending"
            => Period(arguments) is { } period && Text(arguments, "merchant") is { Present: true, Value: not null } merchant
                ? Purchases(period, null, merchant.Value) : null,
        "check_budget" => Budget(arguments),
        _ => null
    };

    // The budget always measures the running month; like the budget service, the merchant wins over the category.
    private static TransactionLinkQuery? Budget(IReadOnlyDictionary<string, string> arguments)
    {
        var merchant = Text(arguments, "merchant");
        var category = Text(arguments, "categoryCode");
        if (merchant.Present && merchant.Value is null || category.Present && category.Value is null)
            return null;
        var month = new PeriodChoice(PeriodKind.ThisMonth, null, null);
        return merchant.Value is not null ? Purchases(month, null, merchant.Value) : Purchases(month, category.Value, null);
    }

    private static TransactionLinkQuery Purchases(PeriodChoice period, string? categoryCode, string? merchant) =>
        new(period.Kind, period.From, period.To, TransactionKind.Purchase, AccountType.Personal, categoryCode, merchant);

    private readonly record struct PeriodChoice(PeriodKind Kind, DateOnly? From, DateOnly? To);

    private static PeriodChoice? Period(IReadOnlyDictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("period", out var name) || !Periods.TryGetValue(name, out var kind))
            return null;
        var hasFrom = arguments.TryGetValue("from", out var from);
        var hasTo = arguments.TryGetValue("to", out var to);
        if (kind != PeriodKind.Custom)
            return hasFrom || hasTo ? null : new PeriodChoice(kind, null, null);
        return Day(from) is { } start && Day(to) is { } end ? new PeriodChoice(kind, start, end) : null;
    }

    private static DateOnly? Day(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    // An absent optional text is fine; a present one must be a non-blank text of a sane length.
    private static (bool Present, string? Value) Text(IReadOnlyDictionary<string, string> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var text))
            return (false, null);
        var trimmed = text.Trim();
        return (true, trimmed.Length is > 0 and <= MerchantMatcher.MaximumLength ? trimmed : null);
    }
}
