using BankingAI.Data.Market;
using Xunit;

namespace BankingAI.Data.Tests.Market;

public class TcmbBulletinParserTests
{
    private static readonly DateOnly Day = new(2025, 10, 10);

    [Fact]
    public void Reads_forex_buying_for_usd_and_eur_under_their_own_symbols()
    {
        var rows = TcmbBulletinParser.Parse(TcmbFixture.Bulletin(), Day);

        Assert.Equal(
            [new MarketPriceRow("USDTRY", Day, 41.6388m, "TCMB"), new MarketPriceRow("EURTRY", Day, 48.1872m, "TCMB")],
            rows.OrderByDescending(r => r.Symbol));
    }

    [Fact]
    public void Other_currencies_are_ignored_even_with_a_unit_other_than_one()
    {
        // JPY is quoted per 100 units in the real file; it must neither fail nor leak into the result.
        var rows = TcmbBulletinParser.Parse(TcmbFixture.Bulletin(), Day);

        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, r => r.Price == 27.4524m);
    }

    [Fact]
    public void A_bulletin_for_another_day_is_refused()
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            TcmbBulletinParser.Parse(TcmbFixture.Bulletin(tarih: "09.10.2025"), Day));
        Assert.Contains("09.10.2025", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("10/10/2025")]
    [InlineData("31.02.2025")]
    public void An_unreadable_bulletin_date_is_refused(string tarih) =>
        Assert.Throws<InvalidDataException>(() => TcmbBulletinParser.Parse(TcmbFixture.Bulletin(tarih: tarih), Day));

    [Theory]
    [InlineData("100")]
    [InlineData("0")]
    [InlineData("")]
    public void A_usd_unit_other_than_one_is_refused(string unit) =>
        Assert.Throws<InvalidDataException>(() => TcmbBulletinParser.Parse(TcmbFixture.Bulletin(usdUnit: unit), Day));

    [Theory]
    [InlineData("")]
    [InlineData("41,6388")]
    [InlineData("41.638,8")]
    [InlineData("-41.6388")]
    [InlineData("0")]
    [InlineData("0.0000")]
    [InlineData("4.16388e1")]
    [InlineData(" 41.6388x")]
    public void An_invalid_forex_buying_value_is_refused(string value) =>
        Assert.Throws<InvalidDataException>(() => TcmbBulletinParser.Parse(TcmbFixture.Bulletin(usdBuying: value), Day));

    [Fact]
    public void A_missing_currency_refuses_the_whole_day()
    {
        Assert.Throws<InvalidDataException>(() => TcmbBulletinParser.Parse(TcmbFixture.Bulletin(includeEur: false), Day));
        Assert.Throws<InvalidDataException>(() => TcmbBulletinParser.Parse(TcmbFixture.Bulletin(includeUsd: false), Day));
    }

    [Fact]
    public void A_currency_listed_twice_is_refused() =>
        Assert.Throws<InvalidDataException>(() => TcmbBulletinParser.Parse(TcmbFixture.Bulletin(duplicateUsd: true), Day));

    [Theory]
    [InlineData("<html><body>Bakım çalışması</body></html>")]
    [InlineData("not xml")]
    [InlineData("")]
    public void Content_that_is_not_a_bulletin_is_refused(string content) =>
        Assert.Throws<InvalidDataException>(() => TcmbBulletinParser.Parse(content, Day));

    [Fact]
    public void A_document_type_declaration_is_refused()
    {
        // Entity expansion is never needed for a bulletin; refusing DTDs closes XML bomb/external entity tricks.
        var xml = TcmbFixture.Bulletin().Replace(
            "<?xml-stylesheet",
            "<!DOCTYPE Tarih_Date [<!ENTITY x \"41.6388\">]><?xml-stylesheet",
            StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => TcmbBulletinParser.Parse(xml, Day));
    }
}
