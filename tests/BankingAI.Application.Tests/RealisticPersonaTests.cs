using BankingAI.Application.Analytics;
using BankingAI.Data;
using BankingAI.Data.Enums;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

// SYN-15: the analyses on the realistic personas' stories. Every expected value follows from the hand-written story
// transactions and the backend's own rules (recurring: ≥3 payments, 26–35 days apart, ±10 % of the median, the last one at
// most 35 days ago), never from the seeder; background noise is only allowed where it cannot change the answer.
public sealed class RealisticPersonaTests
{
    private static BankingDbContext Db()
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        return new BankingDbContext();
    }

    private static PeriodResolver Resolver() => new(new FixedTimeProvider(DemoNowUtc));

    private static async Task<RecurringPayments> RecurringAsync(Guid customer)
    {
        await using var db = Db();
        return await new RecurringPaymentService(db, Resolver()).GetAsync(new RecurringPaymentQuery(customer));
    }

    public static TheoryData<string, string[], string[]> Recurring => new()
    {
        // Murat: water is the same every month; gas and power change with the season and the internet rose by 50 % in August,
        // so none of those three is one recurring payment.
        { "Murat", ["Netflix", "Su Faturası"], ["TRY 609.99"] },
        // Can: Netflix stopped after June (last payment over 35 days ago); Spotify went 99,99 → 109,99, within ±10 % of the median.
        { "Can", ["Spotify"], ["TRY 109.99"] },
        // Zeynep: two lira and two dollar payments, never added together.
        { "Zeynep", ["Adobe", "Figma", "Mobil Hat Faturası", "Ortak Ofis"], ["TRY 3950.00", "USD 37.99"] },
        { "Selin", ["Mobil Hat Faturası", "Spotify"], ["TRY 409.99"] },
        { "Hasan", ["Elektrik Faturası"], ["TRY 780"] },
        // Elif in Berlin: rent and the monthly ticket in euros.
        { "Elif", ["BVG", "Kira"], ["EUR 1408"] },
    };

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [MemberData(nameof(Recurring))]
    public async Task Recurring_payments_follow_the_stories(string persona, string[] merchants, string[] totals)
    {
        var result = await RecurringAsync(persona switch
        {
            "Murat" => Murat, "Can" => Can, "Zeynep" => Zeynep, "Selin" => Selin, "Hasan" => Hasan, _ => Elif
        });

        Assert.Equal(merchants, result.Payments.Select(p => p.Merchant).Order(StringComparer.Ordinal));
        Assert.Equal(totals.Select(t => decimal.Parse(t.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture)),
            result.MonthlyTotals.OrderBy(t => t.CurrencyCode).Select(t => t.MonthlyTotal));
        Assert.Equal(totals.Select(t => t.Split(' ')[0]), result.MonthlyTotals.OrderBy(t => t.CurrencyCode).Select(t => t.CurrencyCode));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Cans_spotify_price_increase_is_still_one_recurring_payment()
    {
        var spotify = Assert.Single((await RecurringAsync(Can)).Payments);

        // April–June 99,99 and July–September 109,99: median 104,99, every amount within 5,00 of it (10 % = 10,50).
        Assert.Equal((6, 109.99m, 99.99m, 109.99m, new DateOnly(2026, 10, 18)),
            (spotify.Pattern.OccurrenceCount, spotify.Pattern.LatestAmount, spotify.Pattern.MinAmount, spotify.Pattern.MaxAmount,
             spotify.Pattern.NextExpectedDate));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Migros_alone_matches_two_of_murats_markets_and_is_asked_back()
    {
        await using var db = Db();

        var result = await new MerchantSpendingService(db, Resolver())
            .GetSpendingAsync(new SpendingQuery(Murat, PeriodKind.Last3Months), "Migros");

        Assert.Equal(MerchantMatchStatus.Ambiguous, result.Match.Status);
        Assert.Equal(["Migros Jet", "Migros Sanal Market"], result.Match.Candidates.Order(StringComparer.Ordinal));
        Assert.Empty(result.Currencies);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Elifs_late_august_purchase_counts_in_her_berlin_august()
    {
        await using var db = Db();
        var search = new TransactionSearchService(db, Resolver());

        async Task<bool> FoundIn(DateOnly from, DateOnly to) =>
            (await search.SearchAsync(new TransactionSearchQuery(Elif, PeriodKind.Custom, from, to, ElifEurAccount,
                MinAmount: 64.30m, MaxAmount: 64.30m, CurrencyCode: "EUR"))).Items.Any(i => i.Amount == 64.30m);

        // 31 August 23:30 in Berlin (21:30 UTC) is 1 September 00:30 in Istanbul; Elif's own time zone decides.
        Assert.True(await FoundIn(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)));
        Assert.False(await FoundIn(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Selin_spent_435_today_and_zeynep_spends_in_two_currencies()
    {
        await using var db = Db();
        var summaries = new SpendingSummaryService(db, Resolver());

        var today = Assert.Single((await summaries.GetAsync(new SpendingQuery(Selin, PeriodKind.Today))).Currencies);
        var zeynep = await summaries.GetAsync(new SpendingQuery(Zeynep, PeriodKind.ThisYear));

        // 10 October: the phone bill (350,00) and one coffee (85,00).
        Assert.Equal(("TRY", 435m, 2), (today.CurrencyCode, today.TotalAmount, today.TransactionCount));
        Assert.Equal(["TRY", "USD"], zeynep.Currencies.Select(c => c.CurrencyCode).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Pocket_money_from_hasan_is_never_spending()
    {
        await using var db = Db();

        var highest = await new HighestTransactionsService(db, Resolver())
            .GetAsync(new HighestTransactionsQuery(Hasan, PeriodKind.LastMonth, Limit: 100));

        // September: 3.000,00 left Hasan's account on the 26th as a transfer; it is not among his purchases.
        var items = Assert.Single(highest.Currencies).Items;
        Assert.All(items, i => Assert.Equal(TransactionKind.Purchase, i.Kind));
        Assert.DoesNotContain(items, i => i.Amount == 3000m);
    }
}
