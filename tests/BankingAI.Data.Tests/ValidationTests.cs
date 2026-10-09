using BankingAI.Data.Entities;
using BankingAI.Data.Enums;
using BankingAI.Data.Validation;
using Xunit;

namespace BankingAI.Data.Tests;

public class ValidationTests
{
    private static readonly DateTimeOffset Reference = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    public static Transaction Transaction() => new()
    {
        AccountId = Guid.NewGuid(), Amount = 12.34m, Direction = TransactionDirection.Debit,
        CurrencyCode = "TRY", RawDescription = "Synthetic purchase",
        OccurredAt = Reference.AddMonths(-6), CreatedAt = Reference
    };

    [Fact]
    public void Money_rejects_loss_and_overflow_without_changing_value()
    {
        foreach (var value in new[] { -1m, 0m, 0.001m, 12.345m, 10000000000000000m, decimal.MaxValue })
            Assert.Throws<ArgumentException>(() => InputRules.Money(value, "Amount", true));
        foreach (var value in new[] { 0.01m, 12m, 12.3m, 12.3400m, 9999999999999999.99m })
            InputRules.Money(value, "Amount", true);
        InputRules.Money(0m, "Balance", false);
        InputRules.Money(0.10m + 0.20m, "Balance", false);
        Assert.Throws<ArgumentException>(() => InputRules.Money(-0.01m, "Balance", false));
        Assert.Throws<ArgumentException>(() => InputRules.Money(12.345m, "Balance", false));
        Assert.Throws<ArgumentException>(() => new Transaction
        {
            AccountId = Guid.NewGuid(), Amount = 12.345m, Direction = TransactionDirection.Debit,
            CurrencyCode = "TRY", RawDescription = "Test", OccurredAt = Reference
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    [InlineData("\u00a0\u2003")]
    [InlineData("abc\0")]
    public void Required_text_rejects_missing_blank_and_nul(string? text) =>
        Assert.Throws<ArgumentException>(() => InputRules.Text(text, 100, "Name"));

    [Fact]
    public void Text_counts_unicode_scalars_and_does_not_trim()
    {
        var text = string.Concat(Enumerable.Repeat("😀", 100));
        InputRules.Text(text, 100, "Name");
        Assert.Throws<ArgumentException>(() => InputRules.Text(text + "a", 100, "Name"));
        Assert.Throws<ArgumentException>(() => InputRules.Text("\ud800", 100, "Name"));
        InputRules.Text(null, 100, "Source", optional: true);
        Assert.Throws<ArgumentException>(() => InputRules.Text(" ", 100, "Source", optional: true));
        InputRules.Text(" a ", 3, "Name");
    }

    [Fact]
    public void All_entity_fields_are_checked_before_persistence()
    {
        void Reject(object entity) => Assert.Throws<ArgumentException>(() => EntityValidation.Validate(entity, Reference));
        Customer Customer(string first = "Demo", string last = "Customer", string currency = "TRY", string zone = "Europe/Istanbul") =>
            new() { FirstName = first, LastName = last, PreferredCurrency = currency, TimeZone = zone, CreatedAt = Reference };
        Reject(new Customer { Id = Guid.Empty, FirstName = "Demo", LastName = "Customer", CreatedAt = Reference });
        Reject(Customer(first: null!)); Reject(Customer(last: "\t"));
        Reject(Customer(first: new string('a', 101))); Reject(Customer(last: new string('a', 101)));
        Reject(Customer(currency: "GBP")); Reject(Customer(zone: "")); Reject(Customer(zone: new string('a', 101)));
        Account Account(string name = "Demo", string number = "DEMO", string currency = "TRY") =>
            new() { CustomerId = Guid.NewGuid(), AccountName = name, AccountNumber = number, CurrencyCode = currency, CreatedAt = Reference };
        Reject(Account(name: "\n")); Reject(Account(name: new string('a', 101)));
        Reject(Account(number: null!)); Reject(Account(number: new string('a', 51)));
        foreach (var code in new[] { "TL", "usd", "TRY ", "US", "" }) Reject(Account(currency: code));
        var t = Transaction(); t.NormalizedMerchant = "\t"; Reject(t);
        t.NormalizedMerchant = new string('a', 151); Reject(t);
        Reject(new TransactionCategory { Id = 0, Code = "FOOD", DisplayName = "Food" });
        Reject(new TransactionCategory { Id = 1, Code = "UNKNOWN", DisplayName = "Unknown" });
        Reject(new TransactionCategory { Id = 1, Code = "FOOD", DisplayName = new string('a', 101) });
        Reject(new TransactionCategory { Id = 1, Code = "FOOD", DisplayName = "\n" });
        MarketAsset Asset(short id = 1, string symbol = "USDTRY", string name = "Dollar", string type = "Currency", string unit = "USD", string quote = "TRY") =>
            new() { Id = id, Symbol = symbol, Name = name, AssetType = type, BaseUnit = unit, QuoteCurrency = quote, IsActive = false };
        Reject(Asset(id: -1)); Reject(Asset(symbol: "")); Reject(Asset(symbol: new string('a', 31)));
        Reject(Asset(name: "\t")); Reject(Asset(name: new string('a', 101))); Reject(Asset(type: "Stock"));
        Reject(Asset(unit: null!)); Reject(Asset(unit: new string('a', 21))); Reject(Asset(quote: "USD"));
        EntityValidation.Validate(Asset(), Reference); // false is a legitimate IsActive value.
        DailyMarketPrice Price(short asset = 1, decimal price = 1m, string? source = null) =>
            new() { MarketAssetId = asset, PriceDate = new DateOnly(2026, 10, 9), Price = price, Source = source };
        Reject(Price(asset: 0)); Reject(Price(price: 0)); Reject(Price(price: -1)); Reject(Price(price: decimal.MaxValue));
        Reject(Price(source: " ")); Reject(Price(source: new string('a', 101)));
        EntityValidation.Validate(Price(), Reference);
    }

    [Fact]
    public void Dates_allow_history_and_reject_future_default_and_non_utc()
    {
        foreach (var date in new[] { DateTimeOffset.MinValue, DateTimeOffset.MaxValue, Reference.AddTicks(1), Reference.ToOffset(TimeSpan.FromHours(3)) })
            Assert.Throws<ArgumentException>(() => InputRules.Timestamp(date, Reference, "OccurredAt"));
        InputRules.Timestamp(Reference.AddYears(-20), Reference, "OccurredAt");
        InputRules.Timestamp(Reference.ToOffset(TimeSpan.FromHours(3)).ToUniversalTime(), Reference, "OccurredAt");
        foreach (var date in new[] { DateOnly.MinValue, DateOnly.MaxValue, new DateOnly(2026, 10, 10) })
            Assert.Throws<ArgumentException>(() => EntityValidation.Validate(new DailyMarketPrice { MarketAssetId = 1, Price = 1m, PriceDate = date }, Reference));
        // 22:00 UTC is already the following calendar day in Istanbul.
        EntityValidation.Validate(new DailyMarketPrice { MarketAssetId = 1, Price = 1m, PriceDate = new(2026, 10, 10) }, Reference.Date.AddHours(22));
        var tx = Transaction(); tx.CategorizedAt = Reference.AddDays(-1); tx.CategoryId = 1; tx.CategorizationSource = CategorizationSource.Rule;
        Assert.Throws<ArgumentException>(() => EntityValidation.Validate(tx, Reference));
    }

    [Fact]
    public void Categorization_checks_every_combination_and_precision()
    {
        foreach (short? id in new short?[] { null, 1 })
        foreach (CategorizationSource? source in new CategorizationSource?[] { null, CategorizationSource.Rule, CategorizationSource.Llm, CategorizationSource.Manual, (CategorizationSource)4 })
        foreach (decimal? score in new decimal?[] { null, 0.85m })
        foreach (DateTimeOffset? at in new DateTimeOffset?[] { null, Reference })
        {
            var valid = id is null ? source is null && score is null && at is null :
                at is not null && (source == CategorizationSource.Llm ? score is not null :
                    (source is CategorizationSource.Rule or CategorizationSource.Manual) && score is null);
            if (valid) InputRules.Categorization(id, source, score, at);
            else Assert.Throws<ArgumentException>(() => InputRules.Categorization(id, source, score, at));
        }
        foreach (var score in new[] { -0.1m, 1.1m, 1.00001m, 0.85001m })
            Assert.Throws<ArgumentException>(() => InputRules.Categorization(1, CategorizationSource.Llm, score, Reference));
        foreach (var score in new[] { 0m, 1m, 0.85000m }) InputRules.Categorization(1, CategorizationSource.Llm, score, Reference);
        Assert.Throws<ArgumentException>(() => InputRules.Categorization(0, CategorizationSource.Rule, null, Reference));
    }

    [Fact]
    public void Failed_category_assignment_preserves_existing_category()
    {
        var tx = Transaction();
        tx.SetCategorization(1, CategorizationSource.Manual);
        var before = (tx.CategoryId, tx.CategorizationSource, tx.CategoryConfidence, tx.CategorizedAt);
        Assert.Throws<ArgumentException>(() => tx.SetCategorization(2, CategorizationSource.Llm, 0.9m));
        Assert.Equal(before, (tx.CategoryId, tx.CategorizationSource, tx.CategoryConfidence, tx.CategorizedAt));
        tx.ClearCategorization();
        Assert.Null(tx.CategoryId); Assert.Null(tx.CategorizationSource); Assert.Null(tx.CategoryConfidence); Assert.Null(tx.CategorizedAt);
    }
}
