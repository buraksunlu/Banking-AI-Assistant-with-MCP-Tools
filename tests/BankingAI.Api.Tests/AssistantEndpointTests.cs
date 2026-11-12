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
    private const string SuggestionsPath = "/api/assistant/suggestions";

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
                 "facts":[{"label":"Toplam harcama","text":"145,00 TL"}],
                 "suggestions":["Kategorilere göre?","Peki işyerlerine göre?"],
                 "choices":[],
                 "links":[{"title":"İşlemleri gör","query":{"period":"Yesterday","from":null,"to":null,"kind":"Purchase",
                           "accountType":"Personal","categoryCode":null,"merchant":null}}]}
                """)!.ToJsonString(),
            body.ToJsonString());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_custom_period_link_carries_the_days_the_answer_used()
    {
        // CHAT-08 + CHAT-07: the date the code read ("1-5 Eylül") is what the list shows.
        var reply = """
            {"type":"answer","answer":"Dönem: 1-5 Eylül 2026. 5.116,50 TL harcadınız.",
             "toolCalls":[{"tool":"get_spending_summary","arguments":{"period":"Custom","from":"2026-09-01","to":"2026-09-05"}}],
             "facts":[],"suggestions":[],"choices":[],"model":"m"}
            """;

        var (status, body) = await AskAsync(FakeAssistantService.Answering(reply));

        var query = Assert.Single(body["links"]!.AsArray())!["query"]!;
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(("Custom", "2026-09-01", "2026-09-05"),
            (query["period"]!.GetValue<string>(), query["from"]!.GetValue<string>(), query["to"]!.GetValue<string>()));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("""{"type":"answer","answer":"x","toolCalls":[{"tool":"get_spending_summary","arguments":{"period":"Yesterdayy"}}],"facts":[],"suggestions":[],"choices":[],"model":"m"}""")]
    [InlineData("""{"type":"answer","answer":"x","toolCalls":[{"tool":"compare_spending","arguments":{"comparison":"PreviousFullMonth"}}],"facts":[],"suggestions":[],"choices":[],"model":"m"}""")]
    [InlineData("""{"type":"clarification","answer":"Hangi dönem?","toolCalls":[],"facts":[],"suggestions":[],"choices":[],"model":"m"}""")]
    public async Task A_reply_without_a_matching_list_still_succeeds_with_no_links(string reply)
    {
        var (status, body) = await AskAsync(FakeAssistantService.Answering(reply));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Empty(body["links"]!.AsArray());
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("clarification", "Clarification")]
    [InlineData("outOfScope", "OutOfScope")]
    public async Task Each_reply_type_keeps_its_meaning(string aiType, string apiType)
    {
        var reply = $$"""{"type":"{{aiType}}","answer":"Hangi dönem?","toolCalls":[],"facts":[],"suggestions":[],"choices":[],"model":"qwen3.5:4b"}""";

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
        // The text and the customer's day (CHAT-08 K9-C); the AI service learns the customer from the token, never from the body.
        var sent = JsonNode.Parse(request.Body!)!.AsObject();
        Assert.Equal(["message", "today", "timeZone"], sent.Select(p => p.Key));
        Assert.Equal("Dün ne harcadım?", sent["message"]!.GetValue<string>());
    }

    // CHAT-08 K9-C: "today" is the business clock's day in the customer's own time zone, so a date the AI service reads
    // ("geçen salı") means what the analyses mean. At 21:30 UTC it is already 11 October in Istanbul, still 10 in Berlin.
    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("10000000-0000-0000-0000-000000000001", "2026-10-11", "Europe/Istanbul")]
    [InlineData("10000000-0000-0000-0000-000000000008", "2026-10-10", "Europe/Berlin")]
    public async Task The_customers_own_day_and_time_zone_go_with_the_message(string customerId, string today, string timeZone)
    {
        var fake = FakeAssistantService.Answering();
        await using var api = FakeAssistantService.Api(fake, settings: [(BankingAI.Hosting.Time.BusinessClock.DemoNowKey, "2026-10-10T21:30:00Z")]);
        using var client = api.CreateClient().WithToken(TestTokens.Create(Guid.Parse(customerId)));

        await PostAsync(client, Message("1-5 Eylül arası ne harcadım?"));

        var sent = JsonNode.Parse(Assert.Single(fake.Requests).Body!)!.AsObject();
        Assert.Equal((today, timeZone), (sent["today"]!.GetValue<string>(), sent["timeZone"]!.GetValue<string>()));
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
            "assistant_unavailable", "Kılavuz şu an kullanılamıyor.");
        AssertProblem((await AskAsync(FakeAssistantService.Failing("ai_timeout", HttpStatusCode.GatewayTimeout))).Body,
            "assistant_timeout", "Kılavuz zamanında cevap veremedi; tekrar deneyin.");
        AssertProblem((await AskAsync(FakeAssistantService.Failing("ai_answer_rejected", HttpStatusCode.BadGateway))).Body,
            "assistant_failed", "Kılavuz bu soruya cevap üretemedi; farklı sormayı deneyin.");
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_questions_choices_reach_the_client_as_written()
    {
        // CHAT-10 K4-A: the rows under a question back; the client sends a label back as the next message (K6-A).
        var reply = """
            {"type":"clarification","answer":"Bunu iki şekilde hesaplayabilirim. Hangisini istersiniz?","toolCalls":[],"facts":[],
             "suggestions":[],"choices":[{"label":"tek seferlik","detail":"bir kerede alsaydınız"},{"label":"aylık birikim","detail":""}],
             "model":"m"}
            """;

        var (status, body) = await AskAsync(FakeAssistantService.Answering(reply));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Clarification", body["type"]!.GetValue<string>());
        Assert.Equal(
            JsonNode.Parse("""[{"label":"tek seferlik","detail":"bir kerede alsaydınız"},{"label":"aylık birikim","detail":""}]""")!.ToJsonString(),
            body["choices"]!.ToJsonString());
        Assert.Empty(body["links"]!.AsArray());
    }

    public static TheoryData<string> BrokenReplies => new(
        "not json",
        "{}",
        """{"type":"smalltalk","answer":"Merhaba","toolCalls":[],"facts":[],"model":"m"}""",
        """{"type":"answer","answer":null,"toolCalls":[],"facts":[],"model":"m"}""",
        """{"type":"answer","answer":"Tamam","toolCalls":[],"model":"m"}""",
        """{"type":"answer","answer":"Tamam","toolCalls":[{"tool":"x"}],"facts":[],"model":"m"}""",
        """{"type":"answer","answer":"Tamam","toolCalls":[],"facts":[{"label":"Toplam"}],"suggestions":[],"choices":[],"model":"m"}""",
        // CHAT-06: the suggestions are part of the contract; a missing list is not read as "none".
        """{"type":"answer","answer":"Tamam","toolCalls":[],"facts":[],"model":"m"}""",
        """{"type":"answer","answer":"Tamam","toolCalls":[],"facts":[],"suggestions":[null],"model":"m"}""",
        """{"type":"answer","answer":"Tamam","toolCalls":[],"facts":[],"suggestions":"Kategorilere göre?","model":"m"}""",
        // CHAT-10 K4-A: the choices too; every one has a label and a detail (empty when the label says it alone).
        """{"type":"clarification","answer":"Hangi dönem?","toolCalls":[],"facts":[],"suggestions":[],"model":"m"}""",
        """{"type":"clarification","answer":"Hangi dönem?","toolCalls":[],"facts":[],"suggestions":[],"choices":[null],"model":"m"}""",
        """{"type":"clarification","answer":"Hangi dönem?","toolCalls":[],"facts":[],"suggestions":[],"choices":[{"label":"bu ay"}],"model":"m"}""",
        """{"type":"clarification","answer":"Hangi dönem?","toolCalls":[],"facts":[],"suggestions":[],"choices":[{"detail":""}],"model":"m"}""",
        """{"type":"clarification","answer":"Hangi dönem?","toolCalls":[],"facts":[],"suggestions":[],"choices":"bu ay","model":"m"}""");

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

    // --- Starter questions (CHAT-06 K4-B) ---------------------------------------------------------------------------

    private static async Task<(HttpStatusCode Status, JsonObject Body)> SuggestionsAsync(FakeAssistantService fake, Guid? customerId = null)
    {
        await using var api = FakeAssistantService.Api(fake);
        using var client = api.CreateClient().WithToken(TestTokens.Create(customerId ?? Ada));
        var response = await client.GetAsync(SuggestionsPath);
        return (response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Starter_questions_come_from_the_ai_service_with_the_callers_own_token()
    {
        var fake = FakeAssistantService.Answering(FakeAssistantService.SampleSuggestions);
        var token = TestTokens.Create(Ada);
        await using var api = FakeAssistantService.Api(fake);
        using var client = api.CreateClient().WithToken(token);

        var response = await client.GetAsync(SuggestionsPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonNode.Parse(FakeAssistantService.SampleSuggestions)!.ToJsonString(),
            JsonNode.Parse(await response.Content.ReadAsStringAsync())!.ToJsonString());
        var request = Assert.Single(fake.Requests);
        // The starters depend on the customer's data, so the AI service needs the token to ask the MCP server.
        Assert.Equal((HttpMethod.Get, "/v1/suggestions", $"Bearer {token}", (string?)null),
            (request.Method, request.Path, request.Authorization, request.Body));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [MemberData(nameof(AiServiceErrors))]
    public async Task Each_starter_error_is_the_same_as_a_messages(
        HttpStatusCode aiStatus, string aiCode, HttpStatusCode expectedStatus, string expectedCode)
    {
        var (status, body) = await SuggestionsAsync(FakeAssistantService.Failing(aiCode, aiStatus));

        Assert.Equal((expectedStatus, expectedCode), (status, body["code"]!.GetValue<string>()));
    }

    public static TheoryData<string> BrokenSuggestions => new(
        "not json", "{}", """{"suggestions":null}""", """{"suggestions":[null]}""", """{"suggestions":"Bu ay?"}""");

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [MemberData(nameof(BrokenSuggestions))]
    public async Task Starters_that_do_not_match_the_contract_are_a_502(string reply)
    {
        var (status, body) = await SuggestionsAsync(FakeAssistantService.Answering(reply));

        Assert.Equal((HttpStatusCode.BadGateway, "assistant_failed"), (status, body["code"]!.GetValue<string>()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Starters_of_an_unreachable_or_slow_ai_service_are_503_and_504()
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await SuggestionsAsync(FakeAssistantService.Unreachable())).Status);

        await using var api = FakeAssistantService.Api(FakeAssistantService.Slow(TimeSpan.FromSeconds(30)),
            settings: [(AssistantSettings.TimeoutKey, "1")]);
        using var client = api.CreateClient().WithToken(TestTokens.Create(Ada));
        Assert.Equal(HttpStatusCode.GatewayTimeout, (await client.GetAsync(SuggestionsPath)).StatusCode);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Starters_of_a_deleted_customer_are_401_before_the_ai_service_is_asked()
    {
        var fake = FakeAssistantService.Answering(FakeAssistantService.SampleSuggestions);

        var (status, body) = await SuggestionsAsync(fake, Guid.NewGuid());

        Assert.Equal((HttpStatusCode.Unauthorized, "session_invalid"), (status, body["code"]!.GetValue<string>()));
        Assert.Empty(fake.Requests);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Opening_the_chat_does_not_use_up_the_ten_messages()
    {
        // The starters run no model (K4-B); counting them would leave fewer questions for a user who reopens the chat.
        var fake = FakeAssistantService.Answering(FakeAssistantService.SampleSuggestions);
        await using var api = FakeAssistantService.Api(fake);
        using var client = api.CreateClient().WithToken(TestTokens.Create(Ada));

        for (var i = 0; i < 11; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(SuggestionsPath)).StatusCode);
    }
}
