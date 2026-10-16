using BankingAI.Data.Entities;
using BankingAI.Data.Enums;
using BankingAI.Data.Security;

namespace BankingAI.Data.Validation;

public static class EntityValidation
{
    public static void Validate(object entity, DateTimeOffset referenceUtc)
    {
        switch (entity)
        {
            case Customer c:
                Id(c.Id, nameof(c.Id));
                InputRules.Text(c.FirstName, 100, nameof(c.FirstName));
                InputRules.Text(c.LastName, 100, nameof(c.LastName));
                InputRules.Require(c.PreferredCurrency is "TRY" or "USD" or "EUR", nameof(c.PreferredCurrency), "TRY/USD/EUR olmalı.");
                InputRules.Text(c.TimeZone, 100, nameof(c.TimeZone));
                // Exact PostgreSQL time-zone membership is checked by the existing DB function.
                InputRules.Timestamp(c.CreatedAt, referenceUtc, nameof(c.CreatedAt));
                break;
            case Account a:
                Id(a.Id, nameof(a.Id));
                Id(a.CustomerId, nameof(a.CustomerId));
                InputRules.Text(a.AccountName, 100, nameof(a.AccountName));
                InputRules.Text(a.AccountNumber, 50, nameof(a.AccountNumber));
                Currency(a.CurrencyCode);
                InputRules.Require(Enum.IsDefined(a.AccountType), nameof(a.AccountType), "Personal veya Business olmalı.");
                InputRules.Money(a.Balance, nameof(a.Balance), positive: false);
                InputRules.Timestamp(a.CreatedAt, referenceUtc, nameof(a.CreatedAt));
                break;
            case Transaction t:
                Id(t.Id, nameof(t.Id));
                Id(t.AccountId, nameof(t.AccountId));
                InputRules.Money(t.Amount, nameof(t.Amount), positive: true);
                InputRules.Require(t.Direction is TransactionDirection.Debit or TransactionDirection.Credit,
                    nameof(t.Direction), "Debit veya Credit olmalı.");
                Currency(t.CurrencyCode);
                InputRules.Text(t.RawDescription, 500, nameof(t.RawDescription));
                InputRules.Text(t.NormalizedMerchant, 150, nameof(t.NormalizedMerchant), optional: true);
                InputRules.Timestamp(t.OccurredAt, referenceUtc, nameof(t.OccurredAt));
                InputRules.Timestamp(t.CreatedAt, referenceUtc, nameof(t.CreatedAt));
                InputRules.Require(t.OccurredAt <= t.CreatedAt, nameof(t.OccurredAt), "Kayıt zamanından ileri olamaz.");
                InputRules.Categorization(t.CategoryId, t.CategorizationSource, t.CategoryConfidence, t.CategorizedAt);
                InputRules.Kind(t.Kind, t.Direction, t.PairId, t.CategoryId);
                if (t.CategorizedAt is { } categorizedAt)
                {
                    InputRules.Timestamp(categorizedAt, referenceUtc, nameof(t.CategorizedAt));
                    InputRules.Require(categorizedAt >= t.CreatedAt, nameof(t.CategorizedAt), "Kayıt zamanından önce olamaz.");
                }
                break;
            case TransactionCategory c:
                Positive(c.Id, nameof(c.Id));
                InputRules.Text(c.Code, 50, nameof(c.Code));
                InputRules.Require(c.Code is "FOOD" or "GROCERY" or "CLOTHING" or "TRANSPORTATION" or
                    "ENTERTAINMENT" or "SUBSCRIPTION" or "TRAVEL" or "FUEL" or "BILLS" or "HEALTH" or "OTHER",
                    nameof(c.Code), "Önceden tanımlı kategori kodu olmalı.");
                InputRules.Text(c.DisplayName, 100, nameof(c.DisplayName));
                break;
            case MarketAsset a:
                Positive(a.Id, nameof(a.Id));
                InputRules.Text(a.Symbol, 30, nameof(a.Symbol));
                InputRules.Text(a.Name, 100, nameof(a.Name));
                InputRules.Text(a.BaseUnit, 20, nameof(a.BaseUnit));
                InputRules.Require(a.AssetType is "Currency" or "Commodity", nameof(a.AssetType), "Currency/Commodity olmalı.");
                InputRules.Require(a.QuoteCurrency == "TRY", nameof(a.QuoteCurrency), "TRY olmalı.");
                break;
            case CustomerCredential c:
                Id(c.CustomerId, nameof(c.CustomerId));
                InputRules.Require(PasswordCredentials.IsValidUsername(c.Username), nameof(c.Username),
                    "3–50 karakter; küçük harf, rakam, nokta, tire veya alt çizgi; harf ya da rakamla başlamalı.");
                InputRules.Text(c.PasswordHash, 256, nameof(c.PasswordHash));
                InputRules.Timestamp(c.CreatedAt, referenceUtc, nameof(c.CreatedAt));
                InputRules.Timestamp(c.UpdatedAt, referenceUtc, nameof(c.UpdatedAt));
                break;
            case DailyMarketPrice p:
                Positive(p.MarketAssetId, nameof(p.MarketAssetId));
                InputRules.Require(p.Price > 0m && p.Price <= 999999999999.99999999m,
                    nameof(p.Price), "Fiyat pozitif ve numeric(20,8) aralığında olmalı.");
                InputRules.Text(p.Source, 100, nameof(p.Source), optional: true);
                var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(referenceUtc, "Europe/Istanbul").DateTime);
                InputRules.Require(p.PriceDate > DateOnly.MinValue && p.PriceDate < DateOnly.MaxValue && p.PriceDate <= today,
                    nameof(p.PriceDate), "Default/MaxValue veya Türkiye takviminde gelecek gün olamaz.");
                break;
        }
    }

    private static void Id(Guid value, string field) =>
        InputRules.Require(value != Guid.Empty, field, "Boş GUID kullanılamaz.");

    private static void Positive(short value, string field) =>
        InputRules.Require(value > 0, field, "Kimlik pozitif olmalı.");

    private static void Currency(string value) => InputRules.Currency(value, "CurrencyCode");
}
