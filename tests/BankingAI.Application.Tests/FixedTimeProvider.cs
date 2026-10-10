namespace BankingAI.Application.Tests;

// Tests choose "now" explicitly so results never depend on the real clock.
internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
