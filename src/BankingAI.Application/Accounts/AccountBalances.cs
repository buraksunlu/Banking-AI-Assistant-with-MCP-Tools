using BankingAI.Data.Enums;

namespace BankingAI.Application.Accounts;

/// <summary>One account as the assistant tells it (ACC-01a): its name, the short number the phone shows, and its balance.</summary>
public sealed record AccountBalance(string AccountName, string ShortNumber, string CurrencyCode, AccountType AccountType, decimal Balance);

/// <summary>The balances of one kind of account in one currency (K4-A: personal and business apart, currencies never added).</summary>
public sealed record BalanceTotal(AccountType AccountType, string CurrencyCode, int Count, decimal Balance);

/// <summary>Every account of the customer and the totals per account type and currency.</summary>
public sealed record AccountOverview(IReadOnlyList<AccountBalance> Accounts, IReadOnlyList<BalanceTotal> Totals);

/// <summary>
/// The balance answer of the assistant (ACC-01a, <c>get_accounts</c>): the account list the API already gives, summed per
/// account type and currency here, in the backend, so neither the model nor the phone adds money up. The whole account number
/// stays out (AI-13): the model and the card get the short number only.
/// </summary>
public static class AccountBalances
{
    /// <summary>Totals ordered personal first, then by currency code; accounts keep the list's order.</summary>
    public static AccountOverview Summarize(IReadOnlyList<AccountDto> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        var totals = accounts
            .GroupBy(a => (a.AccountType, a.CurrencyCode))
            .Select(g => new BalanceTotal(g.Key.AccountType, g.Key.CurrencyCode, g.Count(), g.Sum(a => a.Balance)))
            .OrderBy(t => t.AccountType == AccountType.Personal ? 0 : 1)
            .ThenBy(t => t.CurrencyCode, StringComparer.Ordinal)
            .ToList();
        return new AccountOverview(
            [.. accounts.Select(a => new AccountBalance(a.AccountName, ShortNumber(a.AccountNumber), a.CurrencyCode, a.AccountType, a.Balance))],
            totals);
    }

    /// <summary>
    /// "•••• 001", the rule of the phone's account card (UI-03, <c>AccountDisplay.shortNumber</c>): the part after the last dash,
    /// otherwise the last four characters; a number of four or fewer stays whole.
    /// </summary>
    public static string ShortNumber(string accountNumber)
    {
        var dash = accountNumber.LastIndexOf('-');
        var tail = dash >= 0 && dash < accountNumber.Length - 1 ? accountNumber[(dash + 1)..]
            : accountNumber.Length > 4 ? accountNumber[^4..] : accountNumber;
        return tail == accountNumber && accountNumber.Length <= 4 ? accountNumber : $"•••• {tail}";
    }
}
