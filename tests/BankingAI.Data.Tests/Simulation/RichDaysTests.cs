using BankingAI.Data.Enums;
using BankingAI.Data.Seeding;
using BankingAI.Data.Simulation;
using Xunit;

namespace BankingAI.Data.Tests.Simulation;

// RT-05a: the calendar that makes every tool worth showing on live data. Events repeat every year (T5-A); subscription changes have a
// start or an end date. Amounts are the plan's own hand-picked figures, checked one by one.
public class RichDaysTests
{
    private const short Clothing = 3, Entertainment = 5, Subscription = 6, Travel = 7, Health = 10, Other = 11;

    private static readonly Guid Selin = Account(8), Murat = Account(9), ZeynepTry = Account(10), ZeynepUsd = Account(11),
        Hasan = Account(12), ElifEur = Account(13), ElifTry = Account(14), Can = Account(15), CanShop = Account(16);

    [Theory]
    [InlineData(2026)]
    [InlineData(2027)]
    public void Murat_books_the_republic_day_holiday_every_october(int year)
    {
        var hotel = Assert.Single(DayPlan.For(new DateOnly(year, 10, 20)), p => p.Merchant == "Otel");

        Assert.Equal((Murat, 14800m, Travel), (hotel.AccountId, hotel.Amount, hotel.CategoryId));
    }

    [Theory]
    [InlineData(2026, 27)]
    [InlineData(2027, 26)]
    public void Black_friday_is_the_last_friday_of_november(int year, int day)
    {
        var plan = DayPlan.For(new DateOnly(year, 11, day));
        var week = DayPlan.For(new DateOnly(year, 11, day - 7));

        // T4-A: there is no electronics category; the television is "Diğer".
        Assert.Equal((Murat, 21999m, Other), Select(Assert.Single(plan, p => p.Merchant == "Teknosa")));
        Assert.Equal((ZeynepTry, 4850m, Clothing), Select(Assert.Single(plan, p => p.Merchant == "Zara")));
        Assert.DoesNotContain(week, p => p.Merchant is "Teknosa" or "Zara");
    }

    [Fact]
    public void December_brings_murats_bonus_the_new_year_gifts_and_elifs_flight()
    {
        var december = Month(2026, 12);

        var bonus = Assert.Single(december, p => p.AccountId == Murat && p.RawDescription == "İkramiye ödemesi");
        Assert.Equal((42500m, TransactionKind.Salary, new DateOnly(2026, 12, 15)), (bonus.Amount, bonus.Kind, Istanbul(bonus)));
        Assert.Equal((Murat, 1850m, Entertainment), Select(Assert.Single(december, p => p.Merchant == "Oyuncakçı")));
        Assert.Equal((Selin, 650m, Clothing), Select(Assert.Single(december, p => p.Merchant == "LC Waikiki")));
        var flight = Assert.Single(december, p => p.Merchant == "Havayolu Bileti");
        Assert.Equal((ElifEur, 312m, Travel, "EUR"), (flight.AccountId, flight.Amount, flight.CategoryId, flight.Currency));
    }

    [Fact]
    public void Elifs_istanbul_visit_is_spent_on_her_lira_account()
    {
        var visit = DayPlan.Between(new DateTimeOffset(2026, 12, 20, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 1, 3, 0, 0, 0, TimeSpan.Zero))
            .Where(p => p.AccountId == ElifTry).ToList();

        // 950 + 1.380 + 460 + 720 + 1.650 + 1.400 + 1.450 + 690: two visits fit her 17.610 lira.
        Assert.Equal(8700m, visit.Sum(p => p.Amount));
        Assert.Equal(8, visit.Count);
        Assert.All(visit, p => Assert.Equal(("TRY", TransactionKind.Purchase), (p.Currency, p.Kind)));
    }

    [Fact]
    public void Youtube_premium_starts_in_december_and_figma_ends_after_it()
    {
        Assert.DoesNotContain(DayPlan.For(new DateOnly(2026, 11, 3)), p => p.Merchant == "YouTube Premium");
        foreach (var day in new DateOnly[] { new(2026, 12, 3), new(2027, 1, 3), new(2027, 2, 3) })
            Assert.Equal((Murat, 79.99m, Subscription), Select(Assert.Single(DayPlan.For(day), p => p.Merchant == "YouTube Premium")));

        Assert.Contains(DayPlan.For(new DateOnly(2026, 12, 20)), p => p.Merchant == "Figma" && p.AccountId == ZeynepUsd);
        Assert.DoesNotContain(DayPlan.For(new DateOnly(2027, 1, 20)), p => p.Merchant == "Figma");
    }

    [Fact]
    public void January_raises_netflix_and_hasans_pension()
    {
        // 249,99 is within ±10 % of 229,99's median: still one recurring payment.
        Assert.Equal(229.99m, Assert.Single(DayPlan.For(new DateOnly(2026, 12, 12)), p => p.Merchant == "Netflix").Amount);
        Assert.Equal(249.99m, Assert.Single(DayPlan.For(new DateOnly(2027, 1, 12)), p => p.Merchant == "Netflix").Amount);
        Assert.Equal(22000m, Pension(new DateOnly(2026, 12, 25)));
        Assert.Equal(25300m, Pension(new DateOnly(2027, 1, 25)));

        static decimal Pension(DateOnly day) => Assert.Single(DayPlan.For(day), p => p.AccountId == Hasan && p.Kind == TransactionKind.Salary).Amount;
    }

    [Fact]
    public void January_holds_the_one_off_large_payments_and_the_sales()
    {
        var january = Month(2027, 1);

        Assert.Equal((Murat, 26000m, Travel), Select(Assert.Single(january, p => p.Merchant == "Otel")));
        Assert.Equal((Hasan, 9500m, Health), Select(Assert.Single(january, p => p.Merchant == "Diş Hekimi")));
        Assert.Equal((ZeynepTry, 2600m, Clothing), Select(Assert.Single(january, p => p.Merchant == "Zara")));
        Assert.Equal((Selin, 900m, Clothing), Select(Assert.Single(january, p => p.Merchant == "LC Waikiki")));
    }

    [Fact]
    public void February_and_march_have_valentines_dinners_a_notary_and_a_car_service()
    {
        var plan = Month(2027, 2).Concat(Month(2027, 3)).ToList();

        Assert.Equal(2400m, Assert.Single(plan, p => p.Merchant == "Restoran" && p.AccountId == Can).Amount);
        Assert.Equal(1600m, Assert.Single(plan, p => p.Merchant == "Restoran" && p.AccountId == ZeynepTry).Amount);
        Assert.Equal((Hasan, 1250m, Other), Select(Assert.Single(plan, p => p.Merchant == "Noter")));
        Assert.Equal((Can, 6800m, (short)4), Select(Assert.Single(plan, p => p.Merchant == "Oto Servis")));
    }

    [Fact]
    public void The_catch_up_window_holds_the_holiday_the_concert_the_doctor_and_the_dollar_project()
    {
        var window = DayPlan.Between(SyntheticTransactionSeeder.RealisticCutoff, new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero));

        Assert.Equal(14800m, Assert.Single(window, p => p.Merchant == "Otel").Amount);
        Assert.Equal((Selin, 750m, Entertainment), Select(Assert.Single(window, p => p.Merchant == "Biletix")));
        Assert.Equal((Murat, 1400m, Health), Select(Assert.Single(window, p => p.Merchant == "Çocuk Doktoru")));
        var project = Assert.Single(window, p => p.AccountId == ZeynepUsd && p.Kind == TransactionKind.Salary && p.At.Month == 11 && p.At.Day == 5);
        Assert.Equal((2400m, "USD"), (project.Amount, project.Currency));
    }

    [Fact]
    public void A_purchase_at_cans_shop_is_a_categorised_debit_and_a_sale_of_one_pair()
    {
        // Murat's school shopping every 8 September (T3-A): the shop's income comes from the personas' purchases.
        var supplies = Assert.Single(DayPlan.For(new DateOnly(2027, 9, 8)), p => p.Merchant == "Can Kırtasiye" && p.AccountId == Murat);

        var legs = supplies.ToTransactions();

        Assert.Equal(2500m, supplies.Amount);
        Assert.Equal([(Murat, TransactionDirection.Debit, TransactionKind.Purchase), (CanShop, TransactionDirection.Credit, TransactionKind.Sale)],
            legs.Select(l => (l.AccountId, l.Direction, l.Kind)));
        Assert.Equal(("Can Kırtasiye", (short?)Other, CategorizationSource.Rule), (legs[0].NormalizedMerchant, legs[0].CategoryId, legs[0].CategorizationSource));
        Assert.Equal(("Can Kırtasiye satış geliri", null, null), (legs[1].RawDescription, legs[1].NormalizedMerchant, legs[1].CategoryId));
        Assert.All(legs, l => Assert.Equal(supplies.PairId, l.PairId));
    }

    [Fact]
    public void The_shop_pays_can_what_its_sales_can_carry()
    {
        var transfer = Assert.Single(DayPlan.For(new DateOnly(2026, 12, 1)), p => p.AccountId == CanShop && p.Kind == TransactionKind.Transfer);

        Assert.Equal((3000m, Can), (transfer.Amount, transfer.CounterpartId));
    }

    [Fact]
    public void A_year_uses_every_category_every_account_and_both_kinds_of_pair()
    {
        var year = DayPlan.Between(SyntheticTransactionSeeder.RealisticCutoff, SyntheticTransactionSeeder.RealisticCutoff.AddYears(1));

        Assert.Equal(Enumerable.Range(1, 11).Select(c => (short)c), year.Where(p => p.CategoryId is not null).Select(p => p.CategoryId!.Value).Distinct().Order());
        Assert.Equal(9, year.Select(p => p.AccountId).Distinct().Count());
        Assert.Contains(year, p => p.Kind == TransactionKind.Purchase && p.CounterpartId == CanShop);
        Assert.Contains(year, p => p.Kind == TransactionKind.Transfer);
    }

    [Fact]
    public void A_weekday_moves_through_the_day_at_uneven_minutes()
    {
        // The live feed during a presentation: purchases across the day, not all on the hour.
        // Measured over November's weekdays, so one quiet random day does not decide it.
        var weekdays = Enumerable.Range(1, 30).Select(d => new DateOnly(2026, 11, d))
            .Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList();
        var purchases = weekdays.SelectMany(d => DayPlan.For(d)).Where(p => p.Kind == TransactionKind.Purchase).ToList();

        Assert.True(purchases.Count >= 6 * weekdays.Count, $"only {purchases.Count} purchases on {weekdays.Count} weekdays");
        Assert.True(purchases.Select(p => p.At.ToOffset(TimeSpan.FromHours(3)).Hour).Distinct().Count() >= 10);
        Assert.True(purchases.Count(p => p.At.Minute % 5 != 0) >= purchases.Count / 2);
    }

    private static List<PlannedTransaction> Month(int year, int month) =>
        [.. DayPlan.Between(new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.FromHours(3)),
            new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.FromHours(3)).AddMonths(1).AddTicks(-1))];

    private static DateOnly Istanbul(PlannedTransaction p) => DateOnly.FromDateTime(p.At.ToOffset(TimeSpan.FromHours(3)).DateTime);

    private static (Guid, decimal, short?) Select(PlannedTransaction p) => (p.AccountId, p.Amount, p.CategoryId);

    private static Guid Account(int number) => Guid.Parse($"20000000-0000-0000-0000-{number:D12}");
}
