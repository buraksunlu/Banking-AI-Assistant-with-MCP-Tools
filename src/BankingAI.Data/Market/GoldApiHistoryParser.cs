using System.Globalization;
using System.Text.Json;

namespace BankingAI.Data.Market;

/// <summary>One day's average gold price in US dollars per troy ounce, as gold-api.com reports it.</summary>
public sealed record OuncePrice(DateOnly Day, decimal UsdPerOunce);

/// <summary>
/// Reads the daily XAU history answer of gold-api.com (<c>[{"avg_price":"3991.712202","day":"2025-10-10 00:00:00"}, …]</c>).
/// </summary>
public static class GoldApiHistoryParser
{
    /// <summary>Returns the days in the answer, ordered by day.</summary>
    /// <exception cref="InvalidDataException">The answer is not a valid history array.</exception>
    public static IReadOnlyList<OuncePrice> Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"gold-api cevabı JSON olarak okunamadı: {e.Message}", e);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("gold-api cevabı bir dizi olmalı.");

            var rows = new List<OuncePrice>();
            var seen = new HashSet<DateOnly>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("gold-api satırı bir nesne olmalı.");

                var day = Day(Text(item, "day"));
                if (!seen.Add(day))
                    throw new InvalidDataException($"{day:yyyy-MM-dd} cevapta birden çok kez var.");

                rows.Add(new OuncePrice(day, Price(Text(item, "avg_price"), day)));
            }

            return rows.OrderBy(r => r.Day).ToList();
        }
    }

    private static string Text(JsonElement item, string name)
    {
        // The price is documented as text; a JSON number would already have passed through binary floating point.
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"gold-api satırında metin '{name}' alanı yok.");

        return value.GetString()!;
    }

    private static DateOnly Day(string text)
    {
        if (!DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment)
            || moment.TimeOfDay != TimeSpan.Zero)
            throw new InvalidDataException($"gold-api günü okunamadı: '{text}'.");

        return DateOnly.FromDateTime(moment);
    }

    private static decimal Price(string text, DateOnly day)
    {
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var price) || price <= 0m)
            throw new InvalidDataException($"{day:yyyy-MM-dd} avg_price pozitif bir sayı olmalı; '{text}' bulundu.");

        return price;
    }
}
