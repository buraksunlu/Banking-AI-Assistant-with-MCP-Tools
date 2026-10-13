namespace BankingAI.Data.Writing;

/// <summary>
/// A write reused the identity of an earlier one (the same transaction id or PairId) with different content.
/// Nothing was written. A retry must repeat the original request exactly; a new event needs a new identity.
/// </summary>
public sealed class IdempotencyConflictException()
    : InvalidOperationException("Aynı kimlikle farklı içerikte bir kayıt zaten var; hiçbir şey yazılmadı.");
