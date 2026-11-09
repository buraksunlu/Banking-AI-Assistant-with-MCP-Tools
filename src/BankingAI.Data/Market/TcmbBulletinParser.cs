using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace BankingAI.Data.Market;

/// <summary>
/// Reads the USD and EUR values from one daily TCMB exchange-rate bulletin (<c>kurlar/YYYYMM/DDMMYYYY.xml</c>).
/// </summary>
public static class TcmbBulletinParser
{
    /// <summary>Source name stored with every TCMB price.</summary>
    public const string Source = "TCMB";

    // Currency code in the bulletin → market asset symbol. A code is never written under another symbol.
    private static readonly (string Code, string Symbol)[] Currencies = [("USD", "USDTRY"), ("EUR", "EURTRY")];

    /// <summary>
    /// Returns one row per supported currency, using the bulletin's ForexBuying value.
    /// </summary>
    /// <exception cref="InvalidDataException">The bulletin is malformed, for another day, or misses a currency.</exception>
    public static IReadOnlyList<MarketPriceRow> Parse(string xml, DateOnly expectedDay)
    {
        var root = Load(xml).Root;
        if (root?.Name.LocalName != "Tarih_Date")
            throw new InvalidDataException("TCMB bülteni değil: kök öğe Tarih_Date olmalı.");

        var tarih = (string?)root.Attribute("Tarih") ?? "";
        if (!DateOnly.TryParseExact(tarih, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw new InvalidDataException($"Bülten tarihi okunamadı: '{tarih}'.");
        if (day != expectedDay)
            throw new InvalidDataException(
                $"Bülten {tarih} gününe ait; beklenen {expectedDay.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}.");

        var rows = new List<MarketPriceRow>(Currencies.Length);
        foreach (var (code, symbol) in Currencies)
        {
            var matches = root.Elements("Currency").Where(c => (string?)c.Attribute("CurrencyCode") == code).ToList();
            if (matches.Count != 1)
                throw new InvalidDataException($"{code} bültende bir kez bulunmalı; {matches.Count} kez var.");

            var currency = matches[0];
            var unit = currency.Element("Unit")?.Value.Trim();
            if (unit != "1")
                throw new InvalidDataException($"{code} birimi 1 olmalı; '{unit}' bulundu.");

            rows.Add(new MarketPriceRow(symbol, day, Price(code, currency.Element("ForexBuying")?.Value), Source));
        }

        return rows;
    }

    private static XDocument Load(string xml)
    {
        // DTDs are refused: a bulletin never needs entities, and refusing them rules out entity-expansion tricks.
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            return XDocument.Load(reader);
        }
        catch (XmlException e)
        {
            throw new InvalidDataException($"Bülten XML olarak okunamadı: {e.Message}", e);
        }
    }

    private static decimal Price(string code, string? text)
    {
        // Only digits and one decimal point: a comma, sign, exponent or thousands separator is a different format
        // and must not be guessed.
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var price) || price <= 0m)
            throw new InvalidDataException($"{code} ForexBuying pozitif bir sayı olmalı; '{text}' bulundu.");

        return price;
    }
}
