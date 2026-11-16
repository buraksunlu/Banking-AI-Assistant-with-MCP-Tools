namespace BankingAI.Application.Analytics;

/// <summary>How a counterparty name the user said was resolved against the customer's own transfers (ACC-01a, K3-A).</summary>
public enum CounterpartyMatchStatus
{
    Matched = 1,
    Ambiguous = 2,
    NotFound = 3
}

/// <summary>
/// One account on the other side of the customer's pairs. <c>Key</c> is what may be matched: a person's first name, or the name
/// of a shop or of the customer's own account; <c>Display</c> is what the customer already sees ("Selin K***").
/// </summary>
public sealed record CounterpartyCandidate(Guid AccountId, string Key, string Display);

/// <summary>
/// The outcome: on a match, the shown name and every account of that party (one person may have several); when ambiguous,
/// the shown names to ask about. Account ids stay inside the backend; the API and the tools never return them.
/// </summary>
public sealed record CounterpartyMatch(
    CounterpartyMatchStatus Status, string? Name, IReadOnlyList<string> Candidates,
    [property: System.Text.Json.Serialization.JsonIgnore] IReadOnlyList<Guid> AccountIds);

/// <summary>
/// Resolves untrusted counterparty text against the customer's own counterparties with the merchant rules (exact folded name,
/// then one containing name of at least three letters, never a typo fix). Only names the customer already sees are matched: a
/// person by first name, never by the masked surname.
/// </summary>
public static class CounterpartyMatcher
{
    /// <summary>Matches <paramref name="requested"/>; two different parties behind one name are ambiguous, never merged.</summary>
    public static CounterpartyMatch Match(string requested, IReadOnlyCollection<CounterpartyCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var byKey = MerchantMatcher.Match(requested, candidates.Select(c => c.Key));
        var keys = byKey.Status switch
        {
            MerchantMatchStatus.Matched => [byKey.Merchant!],
            MerchantMatchStatus.Ambiguous => byKey.Candidates,
            _ => []
        };
        var found = candidates.Where(c => keys.Contains(c.Key, StringComparer.Ordinal)).ToList();
        var parties = found.Select(c => c.Display).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return parties.Count switch
        {
            0 => new(CounterpartyMatchStatus.NotFound, null, [], []),
            1 => new(CounterpartyMatchStatus.Matched, parties[0], [], found.Select(c => c.AccountId).Distinct().ToList()),
            _ => new(CounterpartyMatchStatus.Ambiguous, null, parties, [])
        };
    }
}
