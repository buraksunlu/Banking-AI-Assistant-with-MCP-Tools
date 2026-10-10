using System.Text;
using BankingAI.Data.Validation;

namespace BankingAI.Application.Analytics;

public enum MerchantMatchStatus
{
    Matched = 1,
    Ambiguous = 2,
    NotFound = 3
}

// Merchant is the canonical stored name when matched; Candidates lists names only when ambiguous.
public sealed record MerchantMatch(
    MerchantMatchStatus Status, string? Merchant, bool IsPartialMatch, IReadOnlyList<string> Candidates);

// Resolves untrusted merchant text (e.g. a model argument) against the customer's own merchants.
// Exact folded match first, then a single containing name; several names are ambiguous.
// Typos are never auto-corrected: reporting the wrong merchant's amount is worse than "not found".
public static class MerchantMatcher
{
    public const int MaximumLength = 150; // Transactions.NormalizedMerchant varchar(150)
    private const int MinimumPartialLength = 3;

    public static string Fold(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = true;
                continue;
            }
            if (pendingSpace)
                builder.Append(' ');
            pendingSpace = false;
            // A fixed table, not ToUpper/ToLower: under tr-TR "I".ToLower() is "ı", so culture-based
            // casing would make the same name compare differently on different machines.
            builder.Append(character switch
            {
                'ş' or 'Ş' => 'S',
                'ğ' or 'Ğ' => 'G',
                'ı' or 'İ' or 'i' or 'î' or 'Î' => 'I',
                'ü' or 'Ü' or 'û' or 'Û' => 'U',
                'ö' or 'Ö' => 'O',
                'ç' or 'Ç' => 'C',
                'â' or 'Â' => 'A',
                _ => char.ToUpperInvariant(character)
            });
        }
        return builder.ToString();
    }

    public static MerchantMatch Match(string requested, IEnumerable<string> knownMerchants)
    {
        InputRules.Text(requested, MaximumLength, "Merchant");
        ArgumentNullException.ThrowIfNull(knownMerchants);

        var target = Fold(requested);
        var known = knownMerchants.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        var exact = known.Where(m => Fold(m) == target).ToList();
        if (exact.Count == 1)
            return new(MerchantMatchStatus.Matched, exact[0], false, []);
        if (exact.Count > 1)
            return new(MerchantMatchStatus.Ambiguous, null, false, exact);

        // Very short text would "match" almost anything, so it never produces a partial guess.
        if (target.Length < MinimumPartialLength)
            return new(MerchantMatchStatus.NotFound, null, false, []);

        var partial = known.Where(m => Fold(m).Contains(target, StringComparison.Ordinal)).ToList();
        return partial.Count switch
        {
            1 => new(MerchantMatchStatus.Matched, partial[0], true, []),
            > 1 => new(MerchantMatchStatus.Ambiguous, null, false, partial),
            _ => new(MerchantMatchStatus.NotFound, null, false, [])
        };
    }
}
