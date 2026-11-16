using System.Globalization;
using System.Text.RegularExpressions;
using BankingAI.Application.Analytics;
using BankingAI.Data.Enums;

namespace BankingAI.Api.Assistant;

/// <summary>
/// The transaction-list query behind a chat answer's link: what <c>GET /api/transactions</c> is asked so the list
/// shows the rows the answer's figure was computed from. <c>From</c>/<c>To</c> are set only for <c>Custom</c>. ACC-01b PR2: a
/// transfer's direction and person and a search's amount limit (with its currency, never one without the other) are the same
/// filters the answer's tool ran with; null when the tool took none.
/// </summary>
public sealed record TransactionLinkQuery(
    PeriodKind Period, DateOnly? From, DateOnly? To, TransactionKind Kind, AccountType AccountType,
    string? CategoryCode, string? Merchant, TransactionDirection? Direction = null, string? Counterparty = null,
    decimal? MinAmount = null, decimal? MaxAmount = null, string? CurrencyCode = null);

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
        "get_income" => Period(arguments) is { } period
            ? new(period.Kind, period.From, period.To, TransactionKind.Salary, AccountType.Personal, null, null) : null,
        "get_transfers" => Transfers(arguments),
        "search_transactions" => Search(arguments),
        _ => null
    };

    // The MCP server's ToolTransferDirection; left out, both ways.
    private static readonly Dictionary<string, TransactionDirection> Directions = new(StringComparer.Ordinal)
    {
        ["Sent"] = TransactionDirection.Debit, ["Received"] = TransactionDirection.Credit
    };

    private static TransactionLinkQuery? Transfers(IReadOnlyDictionary<string, string> arguments)
    {
        if (Period(arguments) is not { } period)
            return null;
        TransactionDirection? direction = null;
        if (arguments.TryGetValue("direction", out var name))
        {
            if (!Directions.TryGetValue(name, out var known))
                return null;
            direction = known;
        }
        var person = Text(arguments, "counterparty");
        if (person.Present && person.Value is null)
            return null;
        return new(period.Kind, period.From, period.To, TransactionKind.Transfer, AccountType.Personal, null, null, direction, person.Value);
    }

    private static TransactionLinkQuery? Search(IReadOnlyDictionary<string, string> arguments)
    {
        if (Period(arguments) is not { } period)
            return null;
        var merchant = Text(arguments, "merchant");
        var category = Text(arguments, "categoryCode");
        var minimum = Amount(arguments, "minAmount");
        var maximum = Amount(arguments, "maxAmount");
        var currency = Text(arguments, "currencyCode");
        if (merchant.Present && merchant.Value is null || category.Present && category.Value is null
            || minimum.Present && minimum.Value is null || maximum.Present && maximum.Value is null)
            return null;
        // The search takes an amount only with its currency (currencies are never mixed), and a code is three capital letters.
        var hasAmount = minimum.Value is not null || maximum.Value is not null;
        if (hasAmount != currency.Present || currency.Present && !CurrencyCode.IsMatch(currency.Value ?? ""))
            return null;
        return new(period.Kind, period.From, period.To, TransactionKind.Purchase, AccountType.Personal, category.Value, merchant.Value,
            MinAmount: minimum.Value, MaxAmount: maximum.Value, CurrencyCode: currency.Value);
    }

    // An amount the AI service wrote the JSON way ("2000", "5000.5", at most two decimals like the money it filters); a Turkish
    // "2.000", a sign or an exponent is not read.
    private static readonly Regex JsonAmount = new(@"^\d+(\.\d{1,2})?$", RegexOptions.CultureInvariant);
    private static readonly Regex CurrencyCode = new("^[A-Z]{3}$", RegexOptions.CultureInvariant);

    private static (bool Present, decimal? Value) Amount(IReadOnlyDictionary<string, string> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var text))
            return (false, null);
        var amount = JsonAmount.IsMatch(text) ? decimal.Parse(text, CultureInfo.InvariantCulture) : 0m;
        return (true, amount > 0 ? amount : null);
    }

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
