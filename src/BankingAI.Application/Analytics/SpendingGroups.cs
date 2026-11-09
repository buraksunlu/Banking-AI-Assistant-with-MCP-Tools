namespace BankingAI.Application.Analytics;

/// <summary>How a category's spending is treated by the advice rules (INC-02, K3-A wording "Temel / İsteğe bağlı").</summary>
public enum SpendingGroup
{
    /// <summary>Needed spending (Temel); never advised (K8-A).</summary>
    Essential,

    /// <summary>Spending one can choose to cut (İsteğe bağlı); the only group the rules look at.</summary>
    Discretionary,

    /// <summary>OTHER and unclassified purchases (Bilinmiyor); counted, never advised (K4-A).</summary>
    Unknown,
}

/// <summary>
/// The fixed category → group table (INC-02, K1-B, K7-B: code, not a database table). A test requires every category in the
/// database to be listed, so a new category cannot silently fall into <see cref="SpendingGroup.Unknown"/>.
/// </summary>
public static class SpendingGroups
{
    /// <summary>Every category code and its group.</summary>
    public static IReadOnlyDictionary<string, SpendingGroup> Table { get; } = new Dictionary<string, SpendingGroup>(StringComparer.Ordinal)
    {
        ["GROCERY"] = SpendingGroup.Essential,
        // K1-B: eating and drinking is treated as a need, so only clearly optional categories can trigger advice.
        ["FOOD"] = SpendingGroup.Essential,
        ["BILLS"] = SpendingGroup.Essential,
        ["TRANSPORTATION"] = SpendingGroup.Essential,
        ["FUEL"] = SpendingGroup.Essential,
        ["HEALTH"] = SpendingGroup.Essential,
        ["ENTERTAINMENT"] = SpendingGroup.Discretionary,
        ["SUBSCRIPTION"] = SpendingGroup.Discretionary,
        ["CLOTHING"] = SpendingGroup.Discretionary,
        ["TRAVEL"] = SpendingGroup.Discretionary,
        ["OTHER"] = SpendingGroup.Unknown,
    };

    /// <summary>The group of a category code; unclassified (null) and unlisted codes are <see cref="SpendingGroup.Unknown"/>.</summary>
    public static SpendingGroup Of(string? categoryCode) =>
        categoryCode is not null && Table.TryGetValue(categoryCode, out var group) ? group : SpendingGroup.Unknown;
}
