namespace BankingAI.Data.Tests.Market;

/// <summary>
/// Hand-written bulletins shaped like the real TCMB file; values are the 10 Oct 2025 bulletin's
/// (USD 41.6388 / 41.7138, EUR 48.1872 / 48.2740), checked by hand against the raw download.
/// </summary>
internal static class TcmbFixture
{
    public static string Bulletin(
        string tarih = "10.10.2025",
        string usdUnit = "1",
        string usdBuying = "41.6388",
        bool includeUsd = true,
        bool includeEur = true,
        bool duplicateUsd = false)
    {
        var usd = Currency("USD", usdUnit, usdBuying, "41.7138", "41.6097", "41.7764");
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <?xml-stylesheet type="text/xsl" href="isokur.xsl"?>
            <Tarih_Date Tarih="{tarih}" Date="10/10/2025" Bulten_No="2025/193">
            {(includeUsd ? usd : "")}
            {(duplicateUsd ? usd : "")}
            {Currency("JPY", "100", "27.4524", "27.6347", "27.2602", "27.7385")}
            {(includeEur ? Currency("EUR", "1", "48.1872", "48.2740", "48.1535", "48.3463") : "")}
            </Tarih_Date>
            """;
    }

    private static string Currency(string code, string unit, string buying, string selling, string banknoteBuying, string banknoteSelling) => $"""
        <Currency CrossOrder="0" Kod="{code}" CurrencyCode="{code}">
            <Unit>{unit}</Unit>
            <Isim>{code}</Isim>
            <CurrencyName>{code}</CurrencyName>
            <ForexBuying>{buying}</ForexBuying>
            <ForexSelling>{selling}</ForexSelling>
            <BanknoteBuying>{banknoteBuying}</BanknoteBuying>
            <BanknoteSelling>{banknoteSelling}</BanknoteSelling>
            <CrossRateUSD/>
            <CrossRateOther/>
        </Currency>
        """;
}
