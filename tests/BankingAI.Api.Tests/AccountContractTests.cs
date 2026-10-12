using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using BankingAI.Api.Controllers;
using BankingAI.Application.Accounts;
using BankingAI.Data.Enums;
using Xunit;

namespace BankingAI.Api.Tests;

// API-03. The JSON shape of accounts and the rules of the create request, checked with the API's own
// serializer settings. No database: the host only needs placeholder settings to start.
public sealed class AccountContractTests
{
    private static readonly JsonSerializerOptions ApiJson = TestApi.JsonOptions;

    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 9, 14, 8, 38, TimeSpan.Zero);

    [Fact]
    public void Account_json_has_exactly_the_public_fields()
    {
        var dto = new AccountDto(Guid.Parse("20000000-0000-0000-0000-000000000007"), "Kahve Durağı – Kadıköy",
            "DUMMY-DENIZ-SHOP-001", "TRY", AccountType.Business, 2595.00m, CreatedAt);

        var json = JsonNode.Parse(JsonSerializer.Serialize(dto, ApiJson))!.AsObject();

        Assert.Equal(["id", "accountName", "accountNumber", "currencyCode", "accountType", "balance", "createdAt"],
            json.Select(p => p.Key));
        Assert.Equal("Business", json["accountType"]!.GetValue<string>()); // decision 1: names, not numbers
        Assert.Equal("2595.00", json["balance"]!.ToJsonString());           // decision 2: exact JSON number
    }

    [Fact]
    public void Money_is_written_exactly()
    {
        var dto = new AccountDto(Guid.NewGuid(), "Ada günlük TRY", "DUMMY-ADA-TRY-001", "TRY", AccountType.Personal, 577490.18m, CreatedAt);

        Assert.Contains("\"balance\":577490.18,", JsonSerializer.Serialize(dto, ApiJson));
    }

    [Theory]
    [InlineData("\"Business\"", true)]
    [InlineData("2", false)] // numbers are not accepted as enum input either
    [InlineData("\"Corporate\"", false)]
    public void Enum_input_accepts_names_only(string json, bool valid)
    {
        if (valid)
            Assert.Equal(AccountType.Business, JsonSerializer.Deserialize<AccountType>(json, ApiJson));
        else
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AccountType>(json, ApiJson));
    }

    [Theory]
    [InlineData("""{"accountName":"Tatil","currencyCode":"EUR","balance":1000}""")]
    [InlineData("""{"accountName":"Tatil","currencyCode":"EUR","customerId":"10000000-0000-0000-0000-000000000002"}""")]
    [InlineData("""{"accountName":"Tatil","currencyCode":"EUR","accountType":"Business"}""")]
    public void Create_request_rejects_fields_the_client_may_not_set(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreateAccountRequest>(json, ApiJson));

    [Fact]
    public void Create_request_reads_the_allowed_fields()
    {
        var request = JsonSerializer.Deserialize<CreateAccountRequest>("""{"accountName":"Tatil","currencyCode":"EUR"}""", ApiJson);

        Assert.Equal(("Tatil", "EUR"), (request!.AccountName, request.CurrencyCode));
    }

    [Theory]
    [InlineData("Tatil birikimi", "EUR", true)]
    [InlineData("Sterlin", "GBP", true)]
    [InlineData("", "TRY", false)]
    [InlineData("   ", "TRY", false)]
    [InlineData(null, "TRY", false)]
    [InlineData("Tatil", "", false)]
    [InlineData("Tatil", "try", false)] // no silent correction
    [InlineData("Tatil", "JPY", false)]
    public void Create_request_validation(string? accountName, string currencyCode, bool valid)
    {
        var request = new CreateAccountRequest { AccountName = accountName!, CurrencyCode = currencyCode };

        Assert.Equal(valid, Validator.TryValidateObject(request, new ValidationContext(request), [], validateAllProperties: true));
    }

    [Fact]
    public void Account_name_may_have_100_characters_but_not_101()
    {
        static bool Valid(int length)
        {
            var request = new CreateAccountRequest { AccountName = new string('a', length), CurrencyCode = "TRY" };
            return Validator.TryValidateObject(request, new ValidationContext(request), [], validateAllProperties: true);
        }

        Assert.True(Valid(100));
        Assert.False(Valid(101));
    }
}
