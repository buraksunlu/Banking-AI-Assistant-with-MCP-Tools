using System.Text.RegularExpressions;

namespace BankingAI.Application.Analytics;

/// <summary>Why a purchase is in the "Rutin ve temel" group (DEMO-02).</summary>
public enum RoutineReason
{
    /// <summary>A recurring payment of a subscription category (Abonelik).</summary>
    Subscription,

    /// <summary>A recurring payment of the bills category (Fatura).</summary>
    Bill,

    /// <summary>A numbered installment on the statement, from its first payment on (Taksit).</summary>
    Installment,

    /// <summary>A recurring payment of another category (Düzenli ödeme).</summary>
    Recurring,

    /// <summary>Not recurring, but an essential category (Temel).</summary>
    Essential,
}

/// <summary>
/// DEMO-02 K2-A as pure code: an installment number on the statement or the recurring rule makes a purchase routine; otherwise an
/// essential category makes it "Temel"; everything else is non-routine (<c>null</c>).
/// </summary>
public static partial class RoutineClassifier
{
    /// <summary>The installment's number and count when the statement reads "… TAKSIT 03/12" (two digits each), otherwise null.</summary>
    public static (int Number, int Count)? Installment(string rawDescription)
    {
        ArgumentNullException.ThrowIfNull(rawDescription);
        var match = InstallmentPattern().Match(rawDescription);
        return match.Success ? (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value)) : null;
    }

    /// <summary>The purchase's routine reason, or null when it is non-routine.</summary>
    /// <param name="rawDescription">The bank's statement text.</param>
    /// <param name="categoryCode">The purchase's category code; null when unclassified.</param>
    /// <param name="recurring">Whether the recurring rule holds for the purchase's merchant group at the period's end.</param>
    public static RoutineReason? Classify(string rawDescription, string? categoryCode, bool recurring)
    {
        if (Installment(rawDescription) is not null)
            return RoutineReason.Installment;
        if (recurring)
            return categoryCode switch
            {
                "SUBSCRIPTION" => RoutineReason.Subscription,
                "BILLS" => RoutineReason.Bill,
                _ => RoutineReason.Recurring,
            };
        return SpendingGroups.Of(categoryCode) == SpendingGroup.Essential ? RoutineReason.Essential : null;
    }

    // "OKUL TAKSIT ODEMESI" names a payment, not a numbered installment, so the number is required.
    [GeneratedRegex(@"\bTAKSIT (\d{2})/(\d{2})\b", RegexOptions.CultureInvariant)]
    private static partial Regex InstallmentPattern();
}
