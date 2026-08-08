using System.Net;
using System.Net.Http.Json;
using AI.Factory.Core.Copilot;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AI.Factory.IntegrationTests;

public sealed class ReadinessTests : IClassFixture<AiFactoryWebApplicationFactory>
{
    private readonly AiFactoryWebApplicationFactory _factory;
    public ReadinessTests(AiFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_ready_reports_ollama_down_by_default_and_is_admin_only()
    {
        _factory.Ollama.Reset();
        _factory.Ollama.NextReachable = false;

        using var adminClient = CreateClient();
        await LoginAsync(adminClient, "admin.demo");
        using var adminResponse = await adminClient.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, adminResponse.StatusCode);
        var body = (await adminResponse.Content.ReadFromJsonAsync<ReadinessDto>())!;
        Assert.False(body.Healthy);
        Assert.Equal("Healthy", body.Sql);
        Assert.Equal("Unhealthy", body.Ollama);

        using var plannerClient = CreateClient();
        await LoginAsync(plannerClient, "planner.demo");
        using var plannerResponse = await plannerClient.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.Forbidden, plannerResponse.StatusCode);
    }

    [Fact]
    public async Task Health_ready_reports_healthy_when_ollama_is_reachable()
    {
        _factory.Ollama.Reset();
        _factory.Ollama.NextReachable = true;

        using var client = CreateClient();
        await LoginAsync(client, "admin.demo");
        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<ReadinessDto>())!;
        Assert.True(body.Healthy);
        Assert.Equal("Healthy", body.Ollama);
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

    private static async Task<string> GetTokenAsync(HttpClient client) => (await client.GetFromJsonAsync<TokenResponse>("/api/auth/antiforgery"))!.Token;
    private sealed record TokenResponse(string Token);
}
