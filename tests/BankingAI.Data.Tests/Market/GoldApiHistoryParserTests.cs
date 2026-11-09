using BankingAI.Data.Market;
using Xunit;

namespace BankingAI.Data.Tests.Market;

public class GoldApiHistoryParserTests
{
    [Fact]
    public void Reads_the_exact_text_value_and_the_day()
    {
        // Real rows of the gold-api answer (10 and 13 Oct 2025), deliberately out of order.
        const string json = """
            [{"avg_price":"4058.120000","day":"2025-10-13 00:00:00"},
             {"avg_price":"3991.712202","day":"2025-10-10 00:00:00"}]
            """;

        var rows = GoldApiHistoryParser.Parse(json);

        Assert.Equal([new OuncePrice(new(2025, 10, 10), 3991.712202m), new OuncePrice(new(2025, 10, 13), 4058.12m)], rows);
    }

    [Fact]
    public void An_empty_array_is_an_empty_history() => Assert.Empty(GoldApiHistoryParser.Parse("[]"));

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"3991,712202\"")]
    [InlineData("\"-3991.71\"")]
    [InlineData("\"0\"")]
    [InlineData("\"3.99e3\"")]
    [InlineData("\" 3991.71\"")]
    [InlineData("3991.712202")]   // a JSON number instead of the documented text
    [InlineData("null")]
    public void An_invalid_price_is_refused(string price) =>
        Assert.Throws<InvalidDataException>(() =>
            GoldApiHistoryParser.Parse($$"""[{"avg_price":{{price}},"day":"2025-10-10 00:00:00"}]"""));

    [Theory]
    [InlineData("2025-10-10")]
    [InlineData("2025-10-10 12:00:00")]
    [InlineData("10.10.2025 00:00:00")]
    [InlineData("2025-02-30 00:00:00")]
    [InlineData("")]
    public void An_invalid_day_is_refused(string day) =>
        Assert.Throws<InvalidDataException>(() =>
            GoldApiHistoryParser.Parse($$"""[{"avg_price":"3991.712202","day":"{{day}}"}]"""));

    [Fact]
    public void The_same_day_twice_is_refused() =>
        Assert.Throws<InvalidDataException>(() => GoldApiHistoryParser.Parse("""
            [{"avg_price":"3991.712202","day":"2025-10-10 00:00:00"},
             {"avg_price":"3991.712202","day":"2025-10-10 00:00:00"}]
            """));

    [Theory]
    [InlineData("{\"error\":\"Invalid API key\"}")]
    [InlineData("<html>hata</html>")]
    [InlineData("")]
    [InlineData("[{\"day\":\"2025-10-10 00:00:00\"}]")]
    [InlineData("[42]")]
    public void Content_that_is_not_a_history_is_refused(string json) =>
        Assert.Throws<InvalidDataException>(() => GoldApiHistoryParser.Parse(json));
}
