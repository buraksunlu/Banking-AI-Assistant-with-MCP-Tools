using BankingAI.Data.Enums;
using BankingAI.Data.Seeding;
using BankingAI.Data.Simulation;
using Xunit;

namespace BankingAI.Data.Tests.Simulation;

// RT-02a: the day plan of the six SYN-15 personas. The same day always gives the same transactions (S3-A); the fixed payments are
// hand-computed (RT-02 plan), the random background is checked by its rules, never by golden totals of its own.
public class DayPlanTests
{
    private static readonly DateTimeOffset Start = SyntheticTransactionSeeder.RealisticCutoff;
    private static readonly DateTimeOffset Today = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid Selin = Account(8), Murat = Account(9), ZeynepTry = Account(10), ZeynepUsd = Account(11),
        Hasan = Account(12), ElifEur = Account(13), ElifTry = Account(14), Can = Account(15), CanShop = Account(16);

    [Fact]
    public void The_same_day_is_always_the_same_plan()
    {
        var first = DayPlan.For(new DateOnly(2026, 11, 3));
        var second = DayPlan.For(new DateOnly(2026, 11, 3));

        Assert.Equal(first, second);
        Assert.NotEmpty(first);
        Assert.Equal(first.Count, first.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void Murats_salary_comes_on_the_fifteenth_at_nine_in_istanbul()
    {
        var salary = Assert.Single(DayPlan.For(new DateOnly(2026, 10, 15)), p => p.AccountId == Murat && p.Kind == TransactionKind.Salary);

        Assert.Equal((85000m, "TRY", "Maaş ödemesi"), (salary.Amount, salary.Currency, salary.RawDescription));
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 6, 0, 0, TimeSpan.Zero), salary.At);
    }

    [Theory]
    [InlineData(2026, 10, 650, 980)]
    [InlineData(2026, 11, 1850, 1050)]
    [InlineData(2027, 1, 4100, 1310)]
    public void Murats_gas_and_power_follow_the_season_of_the_year_before(int year, int month, int gas, int power)
    {
        var plan = DayPlan.For(new DateOnly(year, month, 20));

        Assert.Equal(gas, Assert.Single(plan, p => p.Merchant == "Doğalgaz Faturası").Amount);
        Assert.Equal(power, Assert.Single(plan, p => p.Merchant == "Elektrik Faturası").Amount);
    }

    [Fact]
    public void The_catch_up_window_holds_murats_hand_computed_fixed_payments()
    {
        var murat = DayPlan.Between(Start, Today).Where(p => p.AccountId == Murat).ToList();

        // 15 October and 15 November.
        Assert.Equal(170000m, murat.Where(p => p.Kind == TransactionKind.Salary).Sum(p => p.Amount));
        // Netflix 12 Oct + 12 Nov, internet 7 Nov (7 Oct was before the cutoff), gas and power 20 Oct, water 22 Oct; 20 Nov not yet.
        string[] fixedMerchants = ["Netflix", "Ev İnterneti", "Doğalgaz Faturası", "Elektrik Faturası", "Su Faturası"];
        Assert.Equal(2919.88m, murat.Where(p => fixedMerchants.Contains(p.Merchant)).Sum(p => p.Amount));
    }

    [Fact]
    public void The_catch_up_window_holds_selins_hand_computed_payments_and_hasans_transfer()
    {
        var window = DayPlan.Between(Start, Today);
        var selin = window.Where(p => p.AccountId == Selin).ToList();

        Assert.Equal(8000m, Assert.Single(selin, p => p.Kind == TransactionKind.Salary).Amount);
        Assert.Equal(59.99m, Assert.Single(selin, p => p.Merchant == "Spotify").Amount);
        Assert.Equal(350m, Assert.Single(selin, p => p.Merchant == "Mobil Hat Faturası").Amount);
        // 26 October: Hasan's monthly 3.000 to Selin, one pair, never spending.
        var transfer = Assert.Single(window, p => p.Kind == TransactionKind.Transfer && p.AccountId == Hasan);
        Assert.Equal((3000m, Selin, new DateOnly(2026, 10, 26)), (transfer.Amount, transfer.CounterpartId, DateOnly.FromDateTime(transfer.At.UtcDateTime)));
        Assert.NotNull(transfer.PairId);
    }

    [Fact]
    public void Elifs_salary_follows_berlin_time_after_the_clocks_go_back()
    {
        // 28 October 2026 is after the end of summer time (25 October): 10:00 in Berlin is 09:00 UTC.
        var salary = Assert.Single(DayPlan.For(new DateOnly(2026, 10, 28)), p => p.AccountId == ElifEur && p.Kind == TransactionKind.Salary);

        Assert.Equal((4200m, "EUR"), (salary.Amount, salary.Currency));
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero), salary.At);
    }

    [Fact]
    public void Zeyneps_dollar_subscriptions_stay_on_her_dollar_account()
    {
        var plan = DayPlan.For(new DateOnly(2026, 11, 9)).Concat(DayPlan.For(new DateOnly(2026, 11, 20))).ToList();

        Assert.Equal((ZeynepUsd, "USD", 22.99m), Select(Assert.Single(plan, p => p.Merchant == "Adobe")));
        Assert.Equal((ZeynepUsd, "USD", 15m), Select(Assert.Single(plan, p => p.Merchant == "Figma")));

        static (Guid, string, decimal) Select(PlannedTransaction p) => (p.AccountId, p.Currency, p.Amount);
    }

    [Fact]
    public void Cans_cancelled_subscriptions_do_not_come_back()
    {
        var sixMonths = DayPlan.Between(Start, Start.AddMonths(6));

        Assert.DoesNotContain(sixMonths, p => p.AccountId == Can && p.Merchant is "Netflix" or "Spotify");
    }

    [Fact]
    public void Nothing_is_planned_at_or_before_the_start_and_everything_is_in_time_order()
    {
        var window = DayPlan.Between(Start, Today);

        Assert.All(window, p => Assert.True(p.At > Start && p.At <= Today));
        Assert.Equal(window.OrderBy(p => p.At).Select(p => p.At), window.Select(p => p.At));
    }

    [Fact]
    public void Half_a_year_has_no_repeated_id_and_only_ever_touches_the_six_personas()
    {
        var plan = DayPlan.Between(Start, Start.AddMonths(6));
        var ids = plan.SelectMany(p => p.PairId is null ? [p.Id] : new[] { p.Id, p.CounterpartLegId!.Value }).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Guid[] personas = [Selin, Murat, ZeynepTry, ZeynepUsd, Hasan, ElifEur, ElifTry, Can, CanShop];
        Assert.All(plan, p => Assert.Contains(p.AccountId, personas));
    }

    [Fact]
    public void Half_a_year_never_overdraws_a_personal_account_and_only_the_shop_runs_short()
    {
        // The live balances at the start (bankingai_live, 16 Nov 2026, equal to the fixed DB). A debit the balance cannot carry is skipped,
        // as the writer refuses it (S8-A); only the shop's monthly 18.000 to Can may be skipped, its sales do not cover it.
        var balances = new Dictionary<Guid, decimal>
        {
            [Selin] = 79772.63m, [Murat] = 644659.42m, [ZeynepTry] = 135422.50m, [ZeynepUsd] = 11771.13m, [Hasan] = 174180.50m,
            [ElifEur] = 28583.11m, [ElifTry] = 17610.00m, [Can] = 72743.21m, [CanShop] = 19720.00m,
        };
        var skipped = new List<PlannedTransaction>();
        foreach (var p in DayPlan.Between(Start, Start.AddMonths(6)))
        {
            var credit = p.Kind is TransactionKind.Salary or TransactionKind.OpeningFunding;
            if (credit)
            {
                balances[p.AccountId] += p.Amount;
                continue;
            }
            if (balances[p.AccountId] < p.Amount)
            {
                skipped.Add(p);
                continue;
            }
            balances[p.AccountId] -= p.Amount;
            if (p.CounterpartId is { } counterpart)
                balances[counterpart] += p.Amount;
        }

        Assert.All(skipped, p => Assert.Equal((CanShop, TransactionKind.Transfer), (p.AccountId, p.Kind)));
        Assert.All(balances.Values, balance => Assert.True(balance >= 0));
    }

    [Fact]
    public void A_planned_purchase_becomes_one_categorised_debit_and_a_transfer_two_legs_of_one_pair()
    {
        var day = DayPlan.For(new DateOnly(2026, 10, 26));
        var transfer = Assert.Single(day, p => p.Kind == TransactionKind.Transfer && p.AccountId == Hasan);
        var purchase = day.First(p => p.Kind == TransactionKind.Purchase && p.CategoryId is not null);

        var legs = transfer.ToTransactions();
        var debit = Assert.Single(purchase.ToTransactions());

        Assert.Equal([TransactionDirection.Debit, TransactionDirection.Credit], legs.Select(l => l.Direction));
        Assert.Equal([Hasan, Selin], legs.Select(l => l.AccountId));
        Assert.All(legs, l => Assert.Equal(transfer.PairId, l.PairId));
        Assert.Equal((purchase.Id, TransactionDirection.Debit, CategorizationSource.Rule), (debit.Id, debit.Direction, debit.CategorizationSource));
    }

    private static Guid Account(int number) => Guid.Parse($"20000000-0000-0000-0000-{number:D12}");
}
