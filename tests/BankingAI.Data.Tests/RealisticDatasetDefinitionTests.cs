using BankingAI.Data.Entities;
using BankingAI.Data.Enums;
using BankingAI.Data.Seeding;
using BankingAI.Data.Validation;
using Xunit;

namespace BankingAI.Data.Tests;

// SYN-15, without a database: the realistic year is built the same way every time, only on the new accounts, never
// overdraws in write order, and its story transactions are the hand-written ones. Expected values are computed by hand
// from the persona rules (12 monthly salaries × amount, and so on), never taken from the seeder.
public sealed class RealisticDatasetDefinitionTests
{
    private static readonly IReadOnlyList<Transaction> Year = SyntheticTransactionSeeder.RealisticDefinitions();

    private static Guid Account(int number) => Guid.Parse($"20000000-0000-0000-0000-{number:D12}");

    private static readonly Guid Selin = Account(8), Murat = Account(9), ZeynepTry = Account(10), ZeynepUsd = Account(11),
        Hasan = Account(12), ElifEur = Account(13), ElifTry = Account(14), Can = Account(15), CanShop = Account(16);

    [Fact]
    public void The_same_year_is_built_on_every_call()
    {
        var again = SyntheticTransactionSeeder.RealisticDefinitions();

        Assert.Equal(Year.Select(Key), again.Select(Key));
    }

    [Fact]
    public void Only_the_new_accounts_are_written_never_the_hand_computed_demo()
    {
        var newAccounts = Enumerable.Range(8, 9).Select(Account).ToHashSet();

        Assert.All(Year, t => Assert.Contains(t.AccountId, newAccounts));
    }

    [Fact]
    public void Identifiers_are_unique_and_inside_the_realistic_blocks()
    {
        Assert.Equal(Year.Count, Year.Select(t => t.Id).Distinct().Count());
        Assert.All(Year, t =>
        {
            var ordinal = long.Parse(t.Id.ToString()[^12..]);
            Assert.InRange(ordinal, 11001, 19999);
            Assert.StartsWith("30000000-0000-0000-0000-", t.Id.ToString());
        });
        Assert.All(Year.Where(t => t.PairId is not null), t => Assert.InRange(long.Parse(t.PairId!.Value.ToString()[^12..]), 101, 999));
    }

    [Fact]
    public void The_year_runs_from_1_october_2025_to_the_demo_moment()
    {
        Assert.Equal(new DateTimeOffset(2025, 10, 1, 3, 0, 0, TimeSpan.Zero), Year.Min(t => t.OccurredAt));  // 06:00 in Istanbul
        Assert.True(Year.Max(t => t.OccurredAt) < new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(Year.Select(t => t.OccurredAt).Order(), Year.Select(t => t.OccurredAt));
    }

    [Fact]
    public void Every_transaction_follows_the_entity_rules_and_pairs_are_whole()
    {
        var reference = DateTimeOffset.UtcNow.AddMinutes(1); // CreatedAt is the moment the entity was built
        Assert.All(Year, t => EntityValidation.Validate(t, reference));
        foreach (var pair in Year.Where(t => t.PairId is not null).GroupBy(t => t.PairId))
        {
            var legs = pair.ToArray();
            Assert.Equal(2, legs.Length);
            Assert.Single(legs, l => l.Direction == TransactionDirection.Debit);
            Assert.Equal((legs[0].Amount, legs[0].CurrencyCode, legs[0].OccurredAt), (legs[1].Amount, legs[1].CurrencyCode, legs[1].OccurredAt));
        }
    }

    [Fact]
    public void No_account_ever_goes_below_zero_in_write_order()
    {
        var balances = new Dictionary<Guid, decimal>();
        foreach (var t in Year)
        {
            balances[t.AccountId] = balances.GetValueOrDefault(t.AccountId) + (t.Direction == TransactionDirection.Credit ? t.Amount : -t.Amount);
            Assert.True(balances[t.AccountId] >= 0, $"{t.AccountId} goes below zero on {t.OccurredAt:yyyy-MM-dd}");
        }
    }

    [Fact]
    public void The_size_is_a_realistic_year()
    {
        Assert.InRange(Year.Count, 1500, 2500);
    }

    // --- story transactions (hand-written, hand-computed) ---

    [Fact]
    public void Monthly_incomes_add_up_by_hand()
    {
        decimal Sum(Guid account, string raw) => Year.Where(t => t.AccountId == account && t.RawDescription == raw).Sum(t => t.Amount);

        Assert.Equal(12 * 85000m, Sum(Murat, "Maaş ödemesi"));        // 15 Oct 2025 … 15 Sep 2026
        Assert.Equal(13 * 8000m, Sum(Selin, "Burs ödemesi"));          // 1 Oct 2025 … 1 Oct 2026
        Assert.Equal(12 * 22000m, Sum(Hasan, "Emekli maaşı"));         // 25 Oct 2025 … 25 Sep 2026
        Assert.Equal(12 * 4200m, Sum(ElifEur, "Gehalt"));              // 28 Oct 2025 … 28 Sep 2026
        Assert.Equal(255500m, Sum(ZeynepTry, "Proje ödemesi"));        // 32.000 + 18.500 + 45.000 + 12.000 + 27.500 + 38.000 + 9.500 + 52.000 + 21.000
        Assert.Equal(11950m, Sum(ZeynepUsd, "Yurt dışı proje ödemesi")); // 1.800 + 2.200 + 1.500 + 2.400 + 1.950 + 2.100
    }

    [Fact]
    public void Transfers_are_pairs_between_the_right_accounts()
    {
        var transfers = Year.Where(t => t.Kind == TransactionKind.Transfer && t.Direction == TransactionDirection.Debit).ToArray();

        Assert.Equal(12, transfers.Count(t => t.AccountId == Hasan && t.Amount == 3000m));   // pocket money to Selin, 26 Oct … 26 Sep
        Assert.Equal(13, transfers.Count(t => t.AccountId == CanShop && t.Amount == 18000m)); // shop → owner, 1 Oct 2025 … 1 Oct 2026
        Assert.Equal(25, transfers.Length);
    }

    [Fact]
    public void Shop_purchases_are_sales_of_can_kirtasiye()
    {
        var sales = Year.Where(t => t.Kind == TransactionKind.Sale).ToArray();

        Assert.All(sales, s => Assert.Equal(CanShop, s.AccountId));
        // Selin 320 + 210 + 480, Murat 640 + 380 + 1.250, Hasan 180 + 260.
        Assert.Equal((8, 3720m), (sales.Length, sales.Sum(s => s.Amount)));
    }

    [Fact]
    public void Can_cancels_netflix_after_june_and_pays_more_for_spotify_from_july()
    {
        var netflix = Year.Where(t => t.AccountId == Can && t.NormalizedMerchant == "Netflix").Select(t => t.OccurredAt).ToArray();
        var spotify = Year.Where(t => t.AccountId == Can && t.NormalizedMerchant == "Spotify").Select(t => (t.OccurredAt.Month, t.Amount)).ToArray();

        Assert.Equal(9, netflix.Length);                                  // October 2025 … June 2026
        Assert.Equal(new DateTimeOffset(2026, 6, 12, 4, 0, 0, TimeSpan.Zero), netflix.Max());
        Assert.Equal(12, spotify.Length);                                 // October 2025 … September 2026
        Assert.All(spotify.Where(s => s.Month is 7 or 8 or 9), s => Assert.Equal(109.99m, s.Amount));
        Assert.All(spotify.Where(s => s.Month is not (7 or 8 or 9)), s => Assert.Equal(99.99m, s.Amount));
    }

    [Fact]
    public void Murats_internet_rises_by_half_in_august()
    {
        var internet = Year.Where(t => t.AccountId == Murat && t.NormalizedMerchant == "Ev İnterneti").ToArray();

        Assert.Equal(10, internet.Count(t => t.Amount == 299.90m));       // October 2025 … July 2026
        Assert.Equal(3, internet.Count(t => t.Amount == 449.90m));        // 7 August, 7 September, 7 October 2026
    }

    [Fact]
    public void Murat_shops_at_two_migros_names_but_never_plain_migros()
    {
        var merchants = Year.Where(t => t.AccountId == Murat).Select(t => t.NormalizedMerchant).ToHashSet();

        Assert.Contains("Migros Jet", merchants);
        Assert.Contains("Migros Sanal Market", merchants);
        Assert.DoesNotContain("Migros", merchants);
    }

    [Fact]
    public void Murats_one_off_and_school_payments()
    {
        Assert.Single(Year, t => t.AccountId == Murat && t.Amount == 45000m && t.CategoryId == 11);                     // white goods, March
        Assert.Equal(50000m, Year.Where(t => t.AccountId == Murat && t.NormalizedMerchant == "Okul Taksidi").Sum(t => t.Amount)); // Feb + Sep
    }

    [Fact]
    public void Elifs_late_august_purchase_is_august_in_berlin_and_september_in_istanbul()
    {
        var boundary = Assert.Single(Year, t => t.AccountId == ElifEur && t.Amount == 64.30m);
        var berlin = TimeZoneInfo.ConvertTime(boundary.OccurredAt, TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));
        var istanbul = TimeZoneInfo.ConvertTime(boundary.OccurredAt, TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"));

        Assert.Equal(new DateTimeOffset(2026, 8, 31, 21, 30, 0, TimeSpan.Zero), boundary.OccurredAt);
        Assert.Equal((8, 9), (berlin.Month, istanbul.Month));
    }

    [Fact]
    public void Elifs_trip_to_turkiye_is_on_her_lira_account_only()
    {
        var trip = Year.Where(t => t.AccountId == ElifTry && t.Kind == TransactionKind.Purchase).ToArray();

        // 860 + 1.240 + 420 + 210 + 600 + 340 + 1.150 + 120 + 1.800 + 650.
        Assert.Equal((10, 7390m), (trip.Length, trip.Sum(t => t.Amount)));
        Assert.DoesNotContain(Year, t => t.AccountId == ElifEur && t.Kind == TransactionKind.Purchase
                                         && t.OccurredAt >= new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.FromHours(2))
                                         && t.OccurredAt < new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.FromHours(2))
                                         && t.NormalizedMerchant is "REWE" or "Lidl" or "Döner Imbiss");
    }

    [Fact]
    public void Zeyneps_dollar_subscriptions_stay_in_dollars()
    {
        var dollars = Year.Where(t => t.AccountId == ZeynepUsd && t.Kind == TransactionKind.Purchase).ToArray();

        Assert.All(dollars, t => Assert.Equal("USD", t.CurrencyCode));
        Assert.Equal(13, dollars.Count(t => t.NormalizedMerchant == "Adobe"));  // 9 Oct 2025 … 9 Oct 2026
        Assert.Equal(12, dollars.Count(t => t.NormalizedMerchant == "Figma"));  // 20 Oct 2025 … 20 Sep 2026
    }

    [Fact]
    public void Selin_is_away_in_august_and_has_one_coffee_on_the_demo_day()
    {
        var august = Year.Where(t => t.AccountId == Selin && t.Kind == TransactionKind.Purchase && t.OccurredAt.Year == 2026 && t.OccurredAt.Month == 8)
            .Select(t => t.NormalizedMerchant!).Order().ToArray();
        var today = Year.Where(t => t.AccountId == Selin && t.OccurredAt.Date == new DateTime(2026, 10, 10) && t.Kind == TransactionKind.Purchase).ToArray();

        // Only the fixed payments (the 26 August transfer is income, not a purchase).
        Assert.Equal(["Mobil Hat Faturası", "Spotify"], august);
        Assert.Equal([("Mobil Hat Faturası", 350m), ("Kampüs Kafe", 85m)], today.Select(t => (t.NormalizedMerchant, t.Amount)).OrderByDescending(x => x.Amount));
    }

    [Fact]
    public void A_card_payment_without_merchant_has_no_category_either()
    {
        var unknown = Year.Where(t => t.Kind == TransactionKind.Purchase && t.NormalizedMerchant is null).ToArray();

        Assert.NotEmpty(unknown);
        Assert.All(unknown, t => Assert.Equal((Can, (short?)null, (CategorizationSource?)null), (t.AccountId, t.CategoryId, t.CategorizationSource)));
    }

    private static string Key(Transaction t) =>
        $"{t.Id}|{t.AccountId}|{t.Amount}|{t.Direction}|{t.Kind}|{t.PairId}|{t.CurrencyCode}|{t.RawDescription}|{t.NormalizedMerchant}|{t.CategoryId}|{t.OccurredAt:O}";
}
