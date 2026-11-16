using BankingAI.Application.Accounts;
using BankingAI.Application.Analytics;
using BankingAI.Data;
using BankingAI.Data.Enums;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

// ACC-01a (16 Nov 2026): the transaction search gives its totals per currency and direction (K2-A) and finds a transfer's other
// side by the name the customer sees (K3-A). Expected values are hand-computed from the seed (docs/synthetic-seed.md).
public sealed class SearchTotalsAndCounterpartyTests
{
    private static readonly Guid MuratTry = Guid.Parse("20000000-0000-0000-0000-000000000009");

    private static async Task<TransactionSearchResult> SearchAsync(TransactionSearchQuery query)
    {
        await using var db = new BankingDbContext();
        return await new TransactionSearchService(db, new PeriodResolver(new FixedTimeProvider(DemoNowUtc))).SearchAsync(query);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Totals_split_money_out_from_money_in_per_currency()
    {
        // Ada's October: purchases 145 + 165 + 95 + 210 + 120 = 735 and the transfer to Deniz 1.500 go out; Ege's 300 and the
        // 45.000 salary come in. A total of both directions would be meaningless, so there is none.
        var result = await SearchAsync(new(Ada, PeriodKind.ThisMonth));

        Assert.Equal([new SearchTotal("TRY", TransactionDirection.Debit, 6, 2235m), new SearchTotal("TRY", TransactionDirection.Credit, 2, 45300m)],
            result.Totals);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Totals_cover_every_page_not_only_the_first()
    {
        // Murat's September purchases of 2.000 lira or more: seven, 41.363,50 in all, on a page of two.
        var result = await SearchAsync(new(Murat, PeriodKind.LastMonth, Kind: TransactionKind.Purchase, Limit: 2, MinAmount: 2000m,
            CurrencyCode: "TRY", AccountType: AccountType.Personal));

        Assert.Equal((7, 2), (result.TotalCount, result.Items.Count));
        Assert.Equal([new SearchTotal("TRY", TransactionDirection.Debit, 7, 41363.50m)], result.Totals);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_salary_search_gives_the_months_income()
    {
        var result = await SearchAsync(new(Murat, PeriodKind.LastMonth, Kind: TransactionKind.Salary, AccountType: AccountType.Personal));

        var salary = Assert.Single(result.Items);
        Assert.Equal((new DateOnly(2026, 9, 15), 85000m, MuratTry), (salary.LocalDate, salary.Amount, salary.AccountId));
        Assert.Equal([new SearchTotal("TRY", TransactionDirection.Credit, 1, 85000m)], result.Totals);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("Selin")]
    [InlineData("selin")]
    [InlineData("SELİN")]
    public async Task A_transfer_is_found_by_the_first_name_the_customer_sees(string name)
    {
        // Hasan's pocket money to Selin on 26 July, August and September: 3 × 3.000.
        var result = await SearchAsync(new(Hasan, PeriodKind.Last3Months, Kind: TransactionKind.Transfer, Direction: TransactionDirection.Debit,
            Counterparty: name));

        Assert.Equal((CounterpartyMatchStatus.Matched, "Selin K***"), (result.CounterpartyMatch!.Status, result.CounterpartyMatch.Name));
        Assert.Equal(3, result.TotalCount);
        Assert.All(result.Items, i => Assert.Equal(("Selin K***", 3000m), (i.Counterparty, i.Amount)));
        Assert.Equal([new SearchTotal("TRY", TransactionDirection.Debit, 3, 9000m)], result.Totals);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_name_no_transfer_has_finds_nothing_and_says_so()
    {
        var result = await SearchAsync(new(Hasan, PeriodKind.Last3Months, Kind: TransactionKind.Transfer, Counterparty: "Ayşe"));

        Assert.Equal(CounterpartyMatchStatus.NotFound, result.CounterpartyMatch!.Status);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
        Assert.Empty(result.Totals);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_shop_on_the_other_side_is_found_by_its_account_name()
    {
        // Ada pays Deniz's shop "Kahve Durağı – Kadıköy" (a Business account: its name is shown, not a person's).
        var result = await SearchAsync(new(Ada, PeriodKind.ThisMonth, Counterparty: "Kahve Durağı"));

        Assert.Equal((CounterpartyMatchStatus.Matched, "Kahve Durağı – Kadıköy"), (result.CounterpartyMatch!.Status, result.CounterpartyMatch.Name));
        Assert.Equal([95m, 120m], result.Items.Select(i => i.Amount).ToArray());
    }

    [Fact]
    public void Two_people_of_one_first_name_are_asked_about_never_merged()
    {
        Guid ali1 = Guid.NewGuid(), ali2 = Guid.NewGuid(), shop = Guid.NewGuid();
        CounterpartyCandidate[] candidates =
        [
            new(ali1, "Ali", "Ali K***"), new(ali2, "Ali", "Ali T***"), new(shop, "Kahve Durağı – Kadıköy", "Kahve Durağı – Kadıköy"),
        ];

        var ali = CounterpartyMatcher.Match("ali", candidates);
        var cafe = CounterpartyMatcher.Match("kahve", candidates);

        Assert.Equal((CounterpartyMatchStatus.Ambiguous, (string?)null), (ali.Status, ali.Name));
        Assert.Equal(["Ali K***", "Ali T***"], ali.Candidates);
        Assert.Empty(ali.AccountIds);
        Assert.Equal((CounterpartyMatchStatus.Matched, "Kahve Durağı – Kadıköy"), (cafe.Status, cafe.Name));
        Assert.Equal([shop], cafe.AccountIds);
    }

    [Fact]
    public void One_person_with_two_accounts_is_one_match()
    {
        Guid first = Guid.NewGuid(), second = Guid.NewGuid();

        var match = CounterpartyMatcher.Match("Elif", [new(first, "Elif", "Elif A***"), new(second, "Elif", "Elif A***")]);

        Assert.Equal((CounterpartyMatchStatus.Matched, "Elif A***"), (match.Status, match.Name));
        Assert.Equal(new[] { first, second }.Order(), match.AccountIds.Order());
    }

    // MARK: Balances (K4-A)

    [Fact]
    public void Balances_are_totalled_per_currency_and_personal_and_business_apart()
    {
        // Can: his lira account and his shop; Zeynep: lira and dollars. Currencies are never added together.
        var now = DateTimeOffset.UnixEpoch;
        AccountDto[] accounts =
        [
            new(Guid.NewGuid(), "Can günlük TRY", "DUMMY-CAN-TRY-001", "TRY", AccountType.Personal, 72743.21m, now),
            new(Guid.NewGuid(), "Can Kırtasiye", "DUMMY-CAN-SHOP-001", "TRY", AccountType.Business, 19720.00m, now),
            new(Guid.NewGuid(), "Zeynep USD", "DUMMY-ZEYNEP-USD-001", "USD", AccountType.Personal, 11771.13m, now),
            new(Guid.NewGuid(), "Zeynep TRY", "DUMMY-ZEYNEP-TRY-001", "TRY", AccountType.Personal, 135422.50m, now),
        ];

        var overview = AccountBalances.Summarize(accounts);

        Assert.Equal(
        [
            new BalanceTotal(AccountType.Personal, "TRY", 2, 208165.71m),
            new BalanceTotal(AccountType.Personal, "USD", 1, 11771.13m),
            new BalanceTotal(AccountType.Business, "TRY", 1, 19720.00m),
        ], overview.Totals);
        Assert.Equal(4, overview.Accounts.Count);
        // The model and the card get the short number only (AI-13): never the whole account number.
        Assert.Equal(["•••• 001", "•••• 001", "•••• 001", "•••• 001"], overview.Accounts.Select(a => a.ShortNumber));
    }
}
