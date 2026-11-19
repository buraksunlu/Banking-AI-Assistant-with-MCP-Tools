using BankingAI.Application.Analytics;
using BankingAI.Data.Entities;
using BankingAI.Data.Enums;
using BankingAI.Data.Seeding;
using BankingAI.Data.Simulation;
using Xunit;

namespace BankingAI.Application.Tests.Demo;

// DEMO-01: Murat, the demo persona, as the live database holds him — the fixed year, the history added before it and the day plan after
// it. No database: every row comes from the plans, every expected value from the design (docs/superpowers/specs/
// 2026-11-19-murat-demo-data-design.md), and the routine-payment check runs the backend's own rule on its own window.
public sealed class MuratStoryTests
{
    private static readonly Guid Murat = Account(9), Selin = Account(8), Can = Account(15);
    private static readonly DateOnly Today = new(2026, 11, 19);
    private static readonly DateTimeOffset Until = new(2026, 11, 19, 21, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    private static readonly string?[] CategoryCodes =
        [null, "FOOD", "GROCERY", "CLOTHING", "TRANSPORTATION", "ENTERTAINMENT", "SUBSCRIPTION", "TRAVEL", "FUEL", "BILLS", "HEALTH", "OTHER"];

    // Every leg of every account the live database gets up to the moment, in the order a running balance reads them.
    private static List<Transaction> AllLegs(DateTimeOffset until) =>
        [.. SyntheticTransactionSeeder.RealisticDefinitions()
            .Concat(DayPlan.MuratBefore(SyntheticTransactionSeeder.RealisticCutoff).SelectMany(p => p.ToTransactions()))
            .Concat(DayPlan.Between(SyntheticTransactionSeeder.RealisticCutoff, until).SelectMany(p => p.ToTransactions()))
            .OrderBy(t => t.OccurredAt).ThenBy(t => t.Direction == TransactionDirection.Credit ? 0 : 1)];

    private static List<Transaction> Timeline(DateTimeOffset until) => [.. AllLegs(until).Where(t => t.AccountId == Murat)];

    private static DateOnly LocalDay(Transaction t) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t.OccurredAt, Istanbul).DateTime);

    private static decimal Signed(Transaction t) => t.Direction == TransactionDirection.Credit ? t.Amount : -t.Amount;

    [Fact]
    public void Murat_has_two_and_a_half_years()
    {
        var timeline = Timeline(Until);

        Assert.Equal(new DateOnly(2024, 5, 15), LocalDay(timeline[0]));
        Assert.True(timeline.Count >= 2000, $"{timeline.Count} rows");
    }

    [Fact]
    public void The_balance_never_goes_below_zero()
    {
        var balance = 0m;
        foreach (var row in Timeline(new DateTimeOffset(2027, 6, 30, 0, 0, 0, TimeSpan.Zero)))
        {
            balance += Signed(row);
            Assert.True(balance >= 0, $"{balance} after {row.RawDescription} at {row.OccurredAt:u}");
        }
    }

    [Fact]
    public void The_history_hands_over_a_modest_balance()
    {
        var handOver = new DateTimeOffset(2025, 10, 1, 3, 0, 0, TimeSpan.Zero);
        var balance = Timeline(Until).Where(t => t.OccurredAt < handOver).Sum(Signed);

        Assert.InRange(balance, 0m, 250_000m);
    }

    [Fact]
    public void Salary_rises_over_the_years()
    {
        var salaries = Timeline(Until).Where(t => t.Kind == TransactionKind.Salary && t.RawDescription == "Maaş ödemesi")
            .ToDictionary(t => (LocalDay(t).Year, LocalDay(t).Month), t => t.Amount);

        Assert.Equal(68_000m, salaries[(2024, 5)]);
        Assert.Equal(68_000m, salaries[(2024, 12)]);
        Assert.Equal(76_000m, salaries[(2025, 1)]);
        Assert.Equal(76_000m, salaries[(2025, 9)]);
        Assert.Equal(85_000m, salaries[(2025, 10)]);
        Assert.Equal(85_000m, salaries[(2026, 11)]);
        Assert.Equal(31, salaries.Count);
        var bonus = Assert.Single(Timeline(Until), t => t.RawDescription == "İkramiye ödemesi" && LocalDay(t) == new DateOnly(2024, 12, 15));
        Assert.Equal(34_000m, bonus.Amount);
    }

    [Theory]
    [InlineData("Apple Store", true)]
    [InlineData("IKEA", true)]
    [InlineData("Spotify", true)]
    [InlineData("iCloud", true)]
    [InlineData("MACFit", true)]
    [InlineData("Turkcell", true)]
    [InlineData("Netflix", true)]
    [InlineData("Su Faturası", true)]
    [InlineData("MediaMarkt", false)]
    [InlineData("Disney+", false)]
    [InlineData("Ev İnterneti", false)]
    public void Routine_payments_on_19_november_2026(string merchant, bool routine)
    {
        var window = new PeriodResolver(new FixedTimeProvider(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero))).Resolve(PeriodKind.Last6Months, "Europe/Istanbul");
        var payments = Timeline(Until)
            .Where(t => t.Kind == TransactionKind.Purchase && t.NormalizedMerchant == merchant)
            .Select(t => new PaymentPoint(LocalDay(t), t.Amount))
            .Where(p => p.Date >= window.StartDate && p.Date <= window.EndDate);

        Assert.Equal(Today, window.EndDate);
        Assert.Equal(routine, RecurringPaymentDetector.Detect(payments, window.EndDate) is not null);
    }

    [Theory]
    [InlineData("Apple Store", "APPLE STORE", 12, 4250, 2026, 6, 5, 6)]
    [InlineData("IKEA", "IKEA", 9, 2800, 2026, 8, 25, 3)]
    [InlineData("MediaMarkt", "MEDIAMARKT", 6, 3000, 2024, 6, 18, 6)]
    public void Installments_are_numbered(string merchant, string raw, int count, int amount, int year, int month, int day, int paidByToday)
    {
        var all = Timeline(new DateTimeOffset(2027, 6, 30, 0, 0, 0, TimeSpan.Zero)).Where(t => t.NormalizedMerchant == merchant).ToList();

        Assert.Equal(count, all.Count);
        for (var n = 1; n <= count; n++)
        {
            var row = all[n - 1];
            Assert.Equal($"{raw} TAKSIT {n:D2}/{count:D2}", row.RawDescription);
            Assert.Equal(amount, row.Amount);
            Assert.Equal(new DateOnly(year, month, day).AddMonths(n - 1), LocalDay(row));
            Assert.Equal((short?)11, row.CategoryId);
        }
        Assert.Equal(paidByToday, all.Count(t => t.OccurredAt <= Until));
    }

    [Fact]
    public void Installment_merchants_appear_nowhere_else()
    {
        var installments = Timeline(new DateTimeOffset(2027, 6, 30, 0, 0, 0, TimeSpan.Zero))
            .Where(t => t.NormalizedMerchant is "Apple Store" or "IKEA" or "MediaMarkt").ToList();

        Assert.Equal(27, installments.Count);
        Assert.All(installments, t => Assert.Contains(" TAKSIT ", t.RawDescription));
    }

    [Fact]
    public void October_2026_is_a_discretionary_month()
    {
        var october = Timeline(Until).Where(t => t.Kind == TransactionKind.Purchase && LocalDay(t) is { Year: 2026, Month: 10 })
            .Where(t => SpendingGroups.Of(t.CategoryId is { } id ? CategoryCodes[id] : null) == SpendingGroup.Discretionary)
            .Sum(t => t.Amount);

        Assert.True(october >= 27_200m, $"{october}");
    }

    [Fact]
    public void School_starts_on_5_september_2026()
    {
        var day = Timeline(Until).Where(t => t.Kind == TransactionKind.Purchase && LocalDay(t) == new DateOnly(2026, 9, 5)).ToList();

        Assert.True(day.Count >= 3, $"{day.Count} purchases");
        Assert.Contains(day, t => t.NormalizedMerchant == "D&R" && t.Amount == 1450m);
        Assert.Contains(day, t => t.NormalizedMerchant == "LC Waikiki" && t.Amount == 2300m);
        Assert.Contains(day, t => t.NormalizedMerchant == "Migros Jet" && t.Amount == 1850m);
    }

    [Fact]
    public void Murat_sends_can_pocket_money_and_selin_pays_him_back()
    {
        var legs = AllLegs(Until);
        var toCan = legs.Where(t => t.AccountId == Murat && t.Kind == TransactionKind.Transfer && t.Direction == TransactionDirection.Debit)
            .Where(t => legs.Any(o => o.PairId == t.PairId && o.AccountId == Can && o.Direction == TransactionDirection.Credit))
            .ToList();

        Assert.Equal(23, toCan.Count); // January 2025 – November 2026
        Assert.All(toCan, t => Assert.Equal((1, 2500m), (LocalDay(t).Day, t.Amount)));
        Assert.Equal(23, toCan.Select(t => (LocalDay(t).Year, LocalDay(t).Month)).Distinct().Count());
        var fromSelin = Assert.Single(legs, t => t.AccountId == Murat && t.Kind == TransactionKind.Transfer
            && t.Direction == TransactionDirection.Credit
            && legs.Any(o => o.PairId == t.PairId && o.AccountId == Selin && o.Direction == TransactionDirection.Debit));
        Assert.Equal((new DateOnly(2026, 11, 3), 3000m), (LocalDay(fromSelin), fromSelin.Amount));
    }

    [Fact]
    public void The_history_adds_only_what_the_fixed_year_lacks()
    {
        var cutoff = SyntheticTransactionSeeder.RealisticCutoff;
        var history = DayPlan.MuratBefore(cutoff).SelectMany(p => p.ToTransactions()).ToList();
        string[] baseline =
        [
            "Maaş ödemesi", "Netflix aylık abonelik ödemesi", "SU FATURA TAHSILATI", "EV INTERNETI FATURA", "DOGALGAZ FATURA TAHSILATI",
            "ELEKTRIK FATURA TAHSILATI", "KIRA ODEMESI",
        ];

        Assert.All(history, t => Assert.True(t.OccurredAt <= cutoff, $"{t.RawDescription} at {t.OccurredAt:u}"));
        Assert.DoesNotContain(history, t => t.OccurredAt >= new DateTimeOffset(2025, 10, 1, 0, 0, 0, TimeSpan.Zero)
            && baseline.Contains(t.RawDescription));
        var fixedIds = SyntheticTransactionSeeder.RealisticDefinitions().Select(t => t.Id).ToHashSet();
        var liveIds = history.Concat(DayPlan.Between(cutoff, Until).SelectMany(p => p.ToTransactions())).Select(t => t.Id).ToList();
        Assert.Equal(liveIds.Count, liveIds.Distinct().Count());
        Assert.DoesNotContain(liveIds, fixedIds.Contains);
    }

    private static Guid Account(int number) => Guid.Parse($"20000000-0000-0000-0000-{number:D12}");
}
