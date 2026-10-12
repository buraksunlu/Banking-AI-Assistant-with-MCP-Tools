using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using BankingAI.Api.Controllers;
using BankingAI.Application.Analytics;
using BankingAI.Data.Enums;
using Xunit;

namespace BankingAI.Api.Tests;

// API-08. The JSON shape of a transaction and the rules of the create request, with the API's own serializer
// settings and no database. The request holds only the event (kind, amount, description, merchant); direction,
// currency, account, time and every category field belong to the backend.
public sealed class TransactionContractTests
{
    private static readonly JsonSerializerOptions ApiJson = TestApi.JsonOptions;

    // Ada's Starbucks purchase of 6 October (30000000-…-602), as the statement returns it.
    private static readonly TransactionItem Starbucks = new(
        Guid.Parse("30000000-0000-0000-0000-000000000602"), Guid.Parse("20000000-0000-0000-0000-000000000001"),
        "Ada günlük TRY", new DateTimeOffset(2026, 10, 6, 9, 0, 0, TimeSpan.Zero), new DateOnly(2026, 10, 6),
        TransactionKind.Purchase, TransactionDirection.Debit, 210.00m, "TRY", "Starbucks", "FOOD", "Yeme-İçme",
        "POS 4412 STARBUCKS KADIKOY", null, false);

    [Fact]
    public void Transaction_json_has_exactly_the_public_fields()
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(Starbucks, ApiJson))!.AsObject();

        Assert.Equal(
            ["id", "accountId", "accountName", "occurredAtUtc", "localDate", "kind", "direction", "amount", "currencyCode",
             "merchant", "categoryCode", "categoryDisplayName", "rawDescription", "counterparty", "counterpartyIsOwnAccount"],
            json.Select(p => p.Key));
        Assert.Equal(("Purchase", "Debit"), (json["kind"]!.GetValue<string>(), json["direction"]!.GetValue<string>()));
        Assert.Equal("210.00", json["amount"]!.ToJsonString());
        Assert.Equal("2026-10-06", json["localDate"]!.GetValue<string>());
    }

    private static CreateTransactionRequest Read(string json) =>
        JsonSerializer.Deserialize<CreateTransactionRequest>(json, ApiJson)!;

    private static bool IsValid(CreateTransactionRequest request) =>
        Validator.TryValidateObject(request, new ValidationContext(request), [], validateAllProperties: true);

    [Theory]
    [InlineData("""{"kind":"Purchase","amount":210.00,"description":"POS 4412 STARBUCKS KADIKOY","merchant":"Starbucks"}""")]
    [InlineData("""{"kind":"Purchase","amount":0.01,"description":"POS 6604 ONLINE ODEME"}""")]
    [InlineData("""{"kind":"Salary","amount":45000,"description":"Maaş ödemesi"}""")]
    [InlineData("""{"kind":"Purchase","amount":9999999999999999.99,"description":"Üst sınır"}""")]
    public void Valid_requests(string json)
    {
        Assert.True(IsValid(Read(json)));
    }

    [Fact]
    public void Valid_request_reads_every_field()
    {
        var request = Read("""{"kind":"Purchase","amount":210.00,"description":"POS 4412 STARBUCKS KADIKOY","merchant":"Starbucks"}""");

        Assert.Equal((TransactionKind.Purchase, 210.00m, "POS 4412 STARBUCKS KADIKOY", "Starbucks"),
            (request.Kind, request.Amount, request.Description, request.Merchant));
    }

    [Theory]
    [InlineData("""{"kind":"Transfer","amount":10,"description":"x"}""")]       // two-legged kinds have their own design
    [InlineData("""{"kind":"Sale","amount":10,"description":"x"}""")]
    [InlineData("""{"kind":"OpeningFunding","amount":10,"description":"x"}""")] // decision 2-B: Purchase and Salary only
    [InlineData("""{"amount":10,"description":"x"}""")]                        // kind missing
    [InlineData("""{"kind":"Purchase","description":"x"}""")]                  // amount missing
    [InlineData("""{"kind":"Purchase","amount":0,"description":"x"}""")]
    [InlineData("""{"kind":"Purchase","amount":-5,"description":"x"}""")]
    [InlineData("""{"kind":"Purchase","amount":12.345,"description":"x"}""")]  // no silent rounding
    [InlineData("""{"kind":"Purchase","amount":10000000000000000.00,"description":"x"}""")] // above the money ceiling
    [InlineData("""{"kind":"Purchase","amount":10}""")]                        // description missing
    [InlineData("""{"kind":"Purchase","amount":10,"description":""}""")]
    [InlineData("""{"kind":"Purchase","amount":10,"description":"   "}""")]
    [InlineData("""{"kind":"Purchase","amount":10,"description":"x","merchant":""}""")]
    [InlineData("""{"kind":"Purchase","amount":10,"description":"x","merchant":"   "}""")]
    [InlineData("""{"kind":"Salary","amount":10,"description":"x","merchant":"Starbucks"}""")] // a salary has no shop
    public void Invalid_requests(string json)
    {
        Assert.False(IsValid(Read(json)));
    }

    [Fact]
    public void Text_lengths_have_upper_limits()
    {
        static CreateTransactionRequest Request(int description, int merchant) => new()
        {
            Kind = TransactionKind.Purchase, Amount = 10m,
            Description = new string('d', description), Merchant = new string('m', merchant)
        };

        Assert.True(IsValid(Request(500, 150)));
        Assert.False(IsValid(Request(501, 150)));
        Assert.False(IsValid(Request(500, 151)));
    }

    [Theory]
    [InlineData("""{"kind":1,"amount":10,"description":"x"}""")]   // enums as names only
    [InlineData("""{"kind":"Purchase","amount":10,"description":"x","direction":"Credit"}""")]
    [InlineData("""{"kind":"Purchase","amount":10,"description":"x","currencyCode":"USD"}""")]
    [InlineData("""{"kind":"Purchase","amount":10,"description":"x","categoryCode":"FOOD"}""")]
    [InlineData("""{"kind":"Purchase","amount":10,"description":"x","occurredAt":"2026-01-01T00:00:00Z"}""")]
    [InlineData("""{"kind":"Purchase","amount":10,"description":"x","accountId":"20000000-0000-0000-0000-000000000003"}""")]
    public void Fields_the_client_may_not_set_are_rejected(string json)
    {
        Assert.Throws<JsonException>(() => Read(json));
    }
}
