using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace BankingAI.Hosting.Time;

/// <summary>
/// The clock of the business: what "this month" means for analytics and when a new transaction happens. It may
/// be fixed to the demo moment with <c>BANKINGAI_DEMO_NOW</c>, so the demo data (built around 10 October 2026)
/// gives the same answers every day. Security never uses it: token lifetimes always follow the real clock.
/// </summary>
public sealed class BusinessClock
{
    /// <summary>Optional setting: a UTC moment such as <c>2026-10-10T12:00:00Z</c>.</summary>
    public const string DemoNowKey = "BANKINGAI_DEMO_NOW";

    private BusinessClock(TimeProvider time) => Time = time;

    /// <summary>The time source for analytics periods and new transactions.</summary>
    public TimeProvider Time { get; }

    /// <summary>Reads the optional demo moment; without it the business follows <paramref name="realTime"/>.</summary>
    /// <exception cref="InvalidOperationException">The demo moment is malformed or in the future.</exception>
    public static BusinessClock FromConfiguration(IConfiguration configuration, TimeProvider realTime)
    {
        var value = configuration[DemoNowKey];
        if (string.IsNullOrWhiteSpace(value))
            return new BusinessClock(realTime);

        // One exact form, always UTC ("Z"), so the demo moment cannot be misread in another time zone.
        if (!DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var demoNow))
            throw new InvalidOperationException($"{DemoNowKey} must be a UTC moment such as 2026-10-10T12:00:00Z.");
        // A future moment would make every new transaction a "future" one, which the writer rejects.
        if (demoNow > realTime.GetUtcNow())
            throw new InvalidOperationException($"{DemoNowKey} cannot be in the future.");
        return new BusinessClock(new FixedTime(demoNow));
    }

    private sealed class FixedTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
