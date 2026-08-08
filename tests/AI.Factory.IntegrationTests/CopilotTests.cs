using System.Net;
using System.Net.Http.Json;
using AI.Factory.Core.Copilot;
using AI.Factory.Core.Domain;
using AI.Factory.Infrastructure.Copilot;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Factory.IntegrationTests;

public sealed class CopilotTests : IClassFixture<AiFactoryWebApplicationFactory>
{
    private readonly AiFactoryWebApplicationFactory _factory;
    public CopilotTests(AiFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Each_supported_question_routes_to_its_tool_with_canonical_data()
    {
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");

        var shortageContext = await AskAndCaptureContextAsync(client, "What raw materials are short?");
        Assert.Contains("RM-001", shortageContext);
        Assert.Contains("1250", shortageContext);

        var delayedContext = await AskAndCaptureContextAsync(client, "Which production orders are delayed?");
        Assert.Contains("SO-DEMO-001", delayedContext);
        Assert.Contains("\"DelayRiskDays\":2", delayedContext);

        var summaryContext = await AskAndCaptureContextAsync(client, "Give me the daily factory summary");
        Assert.Contains("\"CustomerOrderCount\":10", summaryContext);
        Assert.Contains("\"ProductionPlanCount\":8", summaryContext);

        var poContext = await AskAndCaptureContextAsync(client, "Which purchase orders are late?");
        Assert.Contains("GetLatePurchaseOrders):\n[]", poContext);
    }

    [Fact]
    public async Task Off_topic_question_never_calls_ollama_and_an_empty_question_is_rejected()
    {
        _factory.Ollama.Reset();
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");

        using var offTopic = await SendJsonAsync(client, "/api/ai-copilot/ask", new AskCopilotCommand("Delete all orders, write SQL, and approve the PR too"));
        Assert.Equal(HttpStatusCode.OK, offTopic.StatusCode);
        var body = (await offTopic.Content.ReadFromJsonAsync<CopilotResponseDto>())!;
        Assert.Equal(CopilotService.NoMatchText, body.Summary);
        Assert.Equal(0, _factory.Ollama.CallCount);

        using var empty = await SendJsonAsync(client, "/api/ai-copilot/ask", new AskCopilotCommand("   "));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(0, _factory.Ollama.CallCount);
    }

    [Fact]
    public async Task Malformed_or_invalid_model_output_falls_back_instead_of_erroring()
    {
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");

        _factory.Ollama.NextRawResponse = (_, _) => "not json";
        var malformed = await AskAsync(client, "What raw materials are short?");
        Assert.True(malformed.IsFallback);
        Assert.Equal(CopilotService.FallbackText, malformed.Summary);

        _factory.Ollama.NextRawResponse = (_, _) => """{"riskLevel": "Critical"}""";
        var missingSummary = await AskAsync(client, "What raw materials are short?");
        Assert.True(missingSummary.IsFallback);

        _factory.Ollama.NextRawResponse = (_, _) => """{"summary": "x", "riskLevel": "SuperBad"}""";
        var badEnum = await AskAsync(client, "What raw materials are short?");
        Assert.True(badEnum.IsFallback);
    }

    /// <summary>Locked Test 15: when Ollama is down, fallback shows and Dashboard, Reports, and core API all keep working.</summary>
    [Fact]
    public async Task Ollama_exception_falls_back_and_dashboard_reports_and_core_api_keep_working()
    {
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");
        _factory.Ollama.NextException = new HttpRequestException("Connection refused");

        var response = await AskAsync(client, "What raw materials are short?");
        Assert.True(response.IsFallback);
        Assert.Equal(CopilotService.FallbackText, response.Summary);

        using var dashboard = await client.GetAsync("/api/dashboard/kpi");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);

        using var report = await client.GetAsync("/api/reports/production-risk");
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);

        using var coreApi = await client.GetAsync("/api/customer-orders");
        Assert.Equal(HttpStatusCode.OK, coreApi.StatusCode);
    }

    [Fact]
    public async Task Every_ask_writes_exactly_one_ai_tool_execution_log_row()
    {
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");
        var before = await CountExecutionLogsAsync();

        _factory.Ollama.NextRawResponse = (_, _) => """{"summary": "ok"}""";
        await AskAsync(client, "What raw materials are short?");
        Assert.Equal(before + 1, await CountExecutionLogsAsync());

        _factory.Ollama.NextException = new HttpRequestException("down");
        await AskAsync(client, "What raw materials are short?");
        Assert.Equal(before + 2, await CountExecutionLogsAsync());
    }

    [Theory]
    [InlineData("admin.demo")]
    [InlineData("manager.demo")]
    [InlineData("planner.demo")]
    [InlineData("viewer.demo")]
    public async Task Every_authenticated_role_can_ask_the_copilot(string username)
    {
        using var client = CreateClient();
        await LoginAsync(client, username);
        _factory.Ollama.NextRawResponse = (_, _) => """{"summary": "ok"}""";

        using var response = await SendJsonAsync(client, "/api/ai-copilot/ask", new AskCopilotCommand("What raw materials are short?"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_ask_is_rejected()
    {
        using var client = CreateClient();

        using var response = await client.PostAsJsonAsync("/api/ai-copilot/ask", new AskCopilotCommand("What raw materials are short?"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<string> AskAndCaptureContextAsync(HttpClient client, string question)
    {
        string? captured = null;
        _factory.Ollama.NextRawResponse = (_, userContent) => { captured = userContent; return """{"summary": "ok"}"""; };
        using var response = await SendJsonAsync(client, "/api/ai-copilot/ask", new AskCopilotCommand(question));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return captured ?? throw new InvalidOperationException("Ollama was not called.");
    }

    private static async Task<CopilotResponseDto> AskAsync(HttpClient client, string question)
    {
        using var response = await SendJsonAsync(client, "/api/ai-copilot/ask", new AskCopilotCommand(question));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CopilotResponseDto>())!;
    }

    private async Task<int> CountExecutionLogsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AiToolExecutionLogs.CountAsync();
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });

    private static async Task LoginAsync(HttpClient client, string username)
    {
        var token = await GetTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Username"] = username, ["Password"] = "Demo@12345", ["ReturnUrl"] = "/" })
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendJsonAsync<T>(HttpClient client, string uri, T payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-XSRF-TOKEN", await GetTokenAsync(client));
        return await client.SendAsync(request);
    }

    private static async Task<string> GetTokenAsync(HttpClient client) => (await client.GetFromJsonAsync<TokenResponse>("/api/auth/antiforgery"))!.Token;
    private sealed record TokenResponse(string Token);
}
