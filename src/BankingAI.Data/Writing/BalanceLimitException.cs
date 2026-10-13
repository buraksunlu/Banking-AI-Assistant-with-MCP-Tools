namespace BankingAI.Data.Writing;

/// <summary>
/// A write was refused because the account balance would leave its allowed range: below zero (insufficient
/// balance for a Debit) or above the money ceiling (a Credit). Nothing was written. The request itself was
/// valid; the account's current state did not allow it, so the same write may succeed later.
/// </summary>
public sealed class BalanceLimitException(string message) : InvalidOperationException(message);
