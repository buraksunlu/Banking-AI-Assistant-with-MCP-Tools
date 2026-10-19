using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BankingAI.Api.Assistant;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Api.Tests;

// AI-14b + AI-07. The API's chat door, against a fake AI service (FakeAssistantService), so every path runs without a
// model. Tokens are signed for demo customers; these endpoints write nothing, so demo data is never at risk.
public sealed class AssistantEndpointTests
{
    private const string MessagesPath = "/api/assistant/messages";
    private const string StatusPath = "/api/assistant/status";

    private static StringContent Body(string json) => new(json, Encoding.UTF8, "application/json");

    private static StringContent Message(string text) =>
        Body(new JsonObject { ["message"] = text }.ToJsonString());

    private static async Task<(HttpStatusCode Status, JsonObject Body, HttpResponseMessage Response)> PostAsync(
        HttpClient client, HttpContent content)
    {
        var response = await client.PostAsync(MessagesPath, content);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonNode.Parse(text)!.AsObject(), response);
    }

    private static async Task<(HttpStatusCode Status, JsonObject Body)> AskAsync(
        FakeAssistantService fake, string message = "Dün ne harcadım?", Guid? customerId = null, (string, string)[]? settings = null)
    {
        await using var api = FakeAssistantService.Api(fake, settings: settings ?? []);
        using var client = api.CreateClient().WithToken(TestTokens.Create(customerId ?? Ada));
        var (status, body, _) = await PostAsync(client, Message(message));
        return (status, body);
    }

    private static void AssertProblem(JsonObject body, string code, string title)
    {
        Assert.Equal(code, body["code"]!.GetValue<string>());
        Assert.Equal(title, body["title"]!.GetValue<string>());
    }

    // --- Happy path -------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_reply_reaches_the_client_in_the_apis_own_shape()
    {
        var (status, body) = await AskAsync(FakeAssistantService.Answering());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            JsonNode.Parse("""
                {"type":"Answer","answer":"Dün 145,00 TL harcadınız.",
                 "toolCalls":[{"tool":"get_spending_summary","arguments":{"period":"Yesterday"}}],
                 "facts":[{"label":"Toplam harcama","text":"145,00 TL"}]}
                """)!.ToJsonString(),
            body.ToJsonString());
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("clarification", "Clarification")]
    [InlineData("outOfScope", "OutOfScope")]
    public async Task Each_reply_type_keeps_its_meaning(string aiType, string apiType)
    {
        var reply = $$"""{"type":"{{aiType}}","answer":"Hangi dönem?","toolCalls":[],"facts":[],"model":"qwen3.5:4b"}""";

        var (status, body) = await AskAsync(FakeAssistantService.Answering(reply));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal((apiType, "Hangi dönem?"), (body["type"]!.GetValue<string>(), body["answer"]!.GetValue<string>()));
        Assert.Empty(body["toolCalls"]!.AsArray());
        Assert.Empty(body["facts"]!.AsArray());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_message_goes_to_the_ai_service_with_the_callers_own_token_and_nothing_else()
    {
        var fake = FakeAssistantService.Answering();
        var token = TestTokens.Create(Ada);
        await using var api = FakeAssistantService.Api(fake);
        using var client = api.CreateClient().WithToken(token);

        await PostAsync(client, Message("Dün ne harcadım?"));

        var request = Assert.Single(fake.Requests);
        Assert.Equal((HttpMethod.Post, "/v1/chat", $"Bearer {token}"), (request.Method, request.Path, request.Authorization));
        // Only the text: the AI service learns the customer from the token, never from the body.
        var sent = JsonNode.Parse(request.Body!)!.AsObject();
        Assert.Equal(["message"], sent.Select(p => p.Key));
        Assert.Equal("Dün ne harcadım?", sent["message"]!.GetValue<string>());
    }

    // --- The message ------------------------------------------------------------------------------------------------

    public static TheoryData<string> InvalidBodies => new(
        """{"message":""}""",
        """{"message":"   "}""",
        """{"message":null}""",
        """{}""",
        """{"message":"a\u0000b"}""",
        $$"""{"message":"{{new string('a', 501)}}"}""",
        """{"message":"Dün ne harcadım?","customerId":"10000000-0000-0000-0000-000000000002"}""",
        """{"message":123}""");

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [MemberData(nameof(InvalidBodies))]
    public async Task An_invalid_message_is_a_400_and_never_reaches_the_ai_service(string json)
    {
        var fake = FakeAssistantService.Answering();
        await using var api = FakeAssistantService.Api(fake);
        using var client = api.CreateClient().WithToken(TestTokens.Create(Ada));

        var (status, body, _) = await PostAsync(client, Body(json));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("validation_failed", body["code"]!.GetValue<string>());
        Assert.Empty(fake.Requests);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_message_of_exactly_500_characters_is_accepted()
    {
        // 500 Unicode characters; "ı" is one character, as in the database rule.
        var (status, _) = await AskAsync(FakeAssistantService.Answering(), new string('ı', 500));

        Assert.Equal(HttpStatusCode.OK, status);
    }

    // --- Errors of the AI service -----------------------------------------------------------------------------------

    public static TheoryData<HttpStatusCode, string, HttpStatusCode, string> AiServiceErrors => new()
    {
        { HttpStatusCode.ServiceUnavailable, "ai_unavailable", HttpStatusCode.ServiceUnavailable, "assistant_unavailable" },
        { HttpStatusCode.ServiceUnavailable, "model_missing", HttpStatusCode.ServiceUnavailable, "assistant_unavailable" },
        { HttpStatusCode.ServiceUnavailable, "backend_unavailable", HttpStatusCode.ServiceUnavailable, "assistant_unavailable" },
        { HttpStatusCode.GatewayTimeout, "ai_timeout", HttpStatusCode.GatewayTimeout, "assistant_timeout" },
        { HttpStatusCode.BadGateway, "ai_answer_rejected", HttpStatusCode.BadGateway, "assistant_failed" },
        { HttpStatusCode.BadGateway, "ai_bad_tool_call", HttpStatusCode.BadGateway, "assistant_failed" },
        { HttpStatusCode.BadGateway, "ai_bad_response", HttpStatusCode.BadGateway, "assistant_failed" },
        // A contract mismatch between the two services is not the client's fault.
        { HttpStatusCode.BadRequest, "validation_failed", HttpStatusCode.BadGateway, "assistant_failed" },
        { HttpStatusCode.NotFound, "not_found", HttpStatusCode.BadGateway, "assistant_failed" },
        { HttpStatusCode.InternalServerError, "internal_error", HttpStatusCode.BadGateway, "assistant_failed" },
        // The API refused the token when the AI service called it back: the customer is gone mid-turn.
        { HttpStatusCode.Unauthorized, "session_invalid", HttpStatusCode.Unauthorized, "session_invalid" },
    };

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [MemberData(nameof(AiServiceErrors))]
    public async Task Each_ai_service_error_becomes_what_the_client_should_do(
        HttpStatusCode aiStatus, string aiCode, HttpStatusCode expectedStatus, string expectedCode)
    {
        var (status, body) = await AskAsync(FakeAssistantService.Failing(aiCode, aiStatus));

        Assert.Equal(expectedStatus, status);
        Assert.Equal(expectedCode, body["code"]!.GetValue<string>());
        // The AI service's own code is an internal detail; it stays in the server log.
        if (aiCode != expectedCode)
            Assert.DoesNotContain(aiCode, body.ToJsonString());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_three_assistant_errors_have_turkish_titles()
    {
        AssertProblem((await AskAsync(FakeAssistantService.Failing("ai_unavailable", HttpStatusCode.ServiceUnavailable))).Body,
            "assistant_unavailable", "Asistan şu an kullanılamıyor.");
        AssertProblem((await AskAsync(FakeAssistantService.Failing("ai_timeout", HttpStatusCode.GatewayTimeout))).Body,
            "assistant_timeout", "Asistan zamanında cevap veremedi; tekrar deneyin.");
        AssertProblem((await AskAsync(FakeAssistantService.Failing("ai_answer_rejected", HttpStatusCode.BadGateway))).Body,
            "assistant_failed", "Asistan bu soruya cevap üretemedi; farklı sormayı deneyin.");
    }

    public static TheoryData<string> BrokenReplies => new(
        "not json",
        "{}",
        """{"type":"smalltalk","answer":"Merhaba","toolCalls":[],"facts":[],"model":"m"}""",
        """{"type":"answer","answer":null,"toolCalls":[],"facts":[],"model":"m"}""",
        """{"type":"answer","answer":"Tamam","toolCalls":[],"model":"m"}""",
        """{"type":"answer","answer":"Tamam","toolCalls":[{"tool":"x"}],"facts":[],"model":"m"}""",
        """{"type":"answer","answer":"Tamam","toolCalls":[],"facts":[{"label":"Toplam"}],"model":"m"}""");

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [MemberData(nameof(BrokenReplies))]
    public async Task A_reply_that_does_not_match_the_contract_is_a_502(string reply)
    {
        var (status, body) = await AskAsync(FakeAssistantService.Answering(reply));

        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.Equal("assistant_failed", body["code"]!.GetValue<string>());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task An_ai_service_that_is_not_running_is_unavailable()
    {
        var (status, body) = await AskAsync(FakeAssistantService.Unreachable());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("assistant_unavailable", body["code"]!.GetValue<string>());
    }

    // --- Time and cancellation --------------------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task An_ai_service_that_does_not_answer_in_time_is_a_504()
    {
        var fake = FakeAssistantService.Slow(TimeSpan.FromSeconds(30));
        var watch = Stopwatch.StartNew();

        var (status, body) = await AskAsync(fake, settings: [(AssistantSettings.TimeoutKey, "1")]);

        Assert.Equal(HttpStatusCode.GatewayTimeout, status);
        Assert.Equal("assistant_timeout", body["code"]!.GetValue<string>());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"waited {watch.Elapsed}");
        await fake.Cancelled.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_client_that_goes_away_cancels_the_call_to_the_ai_service()
    {
        var fake = FakeAssistantService.Slow(TimeSpan.FromSeconds(30));
        await using var api = FakeAssistantService.Api(fake);
        using var client = api.CreateClient().WithToken(TestTokens.Create(Ada));
        using var leave = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PostAsync(MessagesPath, Message("Dün ne harcadım?"), leave.Token));

        // Well before the 35-second limit: the API passed the client's cancellation on.
        await fake.Cancelled.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // --- Rate limit (SEC-06, decision K6-A) -------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_eleventh_message_within_a_minute_is_429_for_that_customer_only()
    {
        var fake = FakeAssistantService.Answering();
        await using var api = FakeAssistantService.Api(fake);
        using var ada = api.CreateClient().WithToken(TestTokens.Create(Ada));
        using var deniz = api.CreateClient().WithToken(TestTokens.Create(Deniz));

        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await PostAsync(ada, Message("Dün ne harcadım?"))).Status);
        var (status, body, response) = await PostAsync(ada, Message("Dün ne harcadım?"));

        Assert.Equal(HttpStatusCode.TooManyRequests, status);
        Assert.Equal("too_many_requests", body["code"]!.GetValue<string>());
        Assert.InRange(int.Parse(response.Headers.GetValues("Retry-After").Single()), 1, 60);
        Assert.Equal(10, fake.Requests.Count);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(deniz, Message("Dün ne harcadım?"))).Status);
    }

    // --- Status (decision K4-C, K8-A) -------------------------------------------------------------------------------

    private static async Task<(HttpStatusCode Status, JsonObject Body)> StatusAsync(FakeAssistantService fake, Guid? customerId = null)
    {
        await using var api = FakeAssistantService.Api(fake);
        using var client = api.CreateClient().WithToken(TestTokens.Create(customerId ?? Ada));
        var response = await client.GetAsync(StatusPath);
        return (response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Status_is_available_when_the_ai_service_is_ready()
    {
        var fake = FakeAssistantService.Answering("""{"status":"ready","model":"qwen3.5:4b"}""");

        var (status, body) = await StatusAsync(fake);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("""{"available":true}""", body.ToJsonString());
        var request = Assert.Single(fake.Requests);
        // A health check needs no customer: the token stays with the API.
        Assert.Equal((HttpMethod.Get, "/health/ready", (string?)null), (request.Method, request.Path, request.Authorization));
    }

    public static TheoryData<string> NotReady => new("model_missing", "ai_unavailable", "unreachable", "slow");

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [MemberData(nameof(NotReady))]
    public async Task Status_is_unavailable_but_still_200_when_the_ai_service_is_not_ready(string reason)
    {
        var fake = reason switch
        {
            "unreachable" => FakeAssistantService.Unreachable(),
            "slow" => FakeAssistantService.Slow(TimeSpan.FromSeconds(30)),
            _ => FakeAssistantService.Failing(reason, HttpStatusCode.ServiceUnavailable)
        };
        var watch = Stopwatch.StartNew();

        var (status, body) = await StatusAsync(fake);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("""{"available":false}""", body.ToJsonString());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"waited {watch.Elapsed}");
    }

    // --- A customer that no longer exists (decision K10-A) ----------------------------------------------------------

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_deleted_customer_is_told_to_sign_in_again_before_the_ai_service_is_asked()
    {
        var fake = FakeAssistantService.Answering();
        var gone = Guid.NewGuid();

        var (messageStatus, messageBody) = await AskAsync(fake, customerId: gone);
        var (statusStatus, statusBody) = await StatusAsync(fake, gone);

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (messageStatus, statusStatus));
        Assert.Equal("session_invalid", messageBody["code"]!.GetValue<string>());
        Assert.Equal("session_invalid", statusBody["code"]!.GetValue<string>());
        Assert.Empty(fake.Requests);
    }

    // --- Logs (decision K9-A) ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Neither_the_message_nor_the_token_is_ever_logged()
    {
        const string secretQuestion = "Gizli soru 7319: dün ne harcadım?";
        var logs = new CapturedLogs();
        var token = TestTokens.Create(Ada);
        await using (var api = FakeAssistantService.Api(FakeAssistantService.Answering(), logs))
        {
            using var client = api.CreateClient().WithToken(token);
            Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, Message(secretQuestion))).Status);
        }
        await using (var api = FakeAssistantService.Api(FakeAssistantService.Failing("ai_answer_rejected", HttpStatusCode.BadGateway), logs))
        {
            using var client = api.CreateClient().WithToken(token);
            Assert.Equal(HttpStatusCode.BadGateway, (await PostAsync(client, Message(secretQuestion))).Status);
        }

        Assert.DoesNotContain(logs.Lines, line => line.Contains("7319") || line.Contains(token));
        // What is logged instead: enough to follow a turn without the customer's words.
        Assert.Contains(logs.Lines, line => line.Contains("Answer") && line.Contains("get_spending_summary") && line.Contains("qwen3.5:4b"));
        Assert.Contains(logs.Lines, line => line.Contains("ai_answer_rejected"));
    }
}
