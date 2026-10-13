namespace BankingAI.Application;

/// <summary>
/// The customer a request is made for does not exist (for example, deleted after its token was issued). Kept
/// apart from a missing account or transaction, which stays a plain <see cref="KeyNotFoundException"/>: the API
/// answers this one with 401 (sign in again) and those with 404.
/// </summary>
public sealed class CustomerNotFoundException() : KeyNotFoundException("Müşteri bulunamadı.");
