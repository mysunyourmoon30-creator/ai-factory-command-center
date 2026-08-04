using System.Net;
using System.Net.Http.Json;
using AI.Factory.Core.Domain;
using AI.Factory.Core.Security;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Factory.IntegrationTests;

public sealed class AuthenticationTests : IClassFixture<AiFactoryWebApplicationFactory>
{
    private readonly AiFactoryWebApplicationFactory _factory;

    public AuthenticationTests(AiFactoryWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Planner_login_creates_secure_cookie_and_success_audit()
    {
        using var client = CreateClient();
        var response = await LoginAsync(client, "planner.demo", "Demo@12345");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        var authCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("AI.Factory.Auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", authCookie, StringComparison.OrdinalIgnoreCase);

        var currentUser = await client.GetFromJsonAsync<CurrentUser>("/api/auth/me");
        Assert.NotNull(currentUser);
        Assert.Equal("planner.demo", currentUser.Username);
        Assert.Contains(RoleNames.Planner, currentUser.Roles);
        Assert.True(await AuditExistsAsync("Login Success", "planner.demo"));
    }

    [Theory]
    [InlineData("admin.demo", RoleNames.Admin)]
    [InlineData("manager.demo", RoleNames.Manager)]
    [InlineData("planner.demo", RoleNames.Planner)]
    [InlineData("viewer.demo", RoleNames.Viewer)]
    public async Task All_demo_users_can_login_with_their_locked_role(string username, string role)
    {
        using var client = CreateClient();
        using var response = await LoginAsync(client, username, "Demo@12345");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var currentUser = await client.GetFromJsonAsync<CurrentUser>("/api/auth/me");
        Assert.NotNull(currentUser);
        Assert.Equal(username, currentUser.Username);
        Assert.Contains(role, currentUser.Roles);
    }

    [Fact]
    public async Task Invalid_password_creates_failure_audit_without_auth_cookie()
    {
        using var client = CreateClient();
        var response = await LoginAsync(client, "planner.demo", "wrong-password");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login?error=invalid", response.Headers.Location?.OriginalString);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) &&
            cookies.Any(value => value.StartsWith("AI.Factory.Auth=", StringComparison.Ordinal)));
        Assert.True(await AuditExistsAsync("Login Failure", "planner.demo"));
    }

    [Fact]
    public async Task Login_without_antiforgery_token_is_rejected()
    {
        using var client = CreateClient();
        using var response = await client.PostAsync("/api/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "planner.demo",
            ["Password"] = "Demo@12345",
            ["ReturnUrl"] = "/"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_cannot_use_admin_write_endpoint_and_attempt_is_audited()
    {
        using var client = CreateClient();
        using var loginResponse = await LoginAsync(client, "viewer.demo", "Demo@12345");
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);

        var viewer = await client.GetFromJsonAsync<CurrentUser>("/api/auth/me");
        Assert.NotNull(viewer);
        var token = await GetAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{viewer.Id}/activation")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["IsActive"] = "false" })
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(await AuditExistsAsync("Unauthorized Access", "viewer.demo"));
    }

    [Fact]
    public async Task Logout_clears_session_and_creates_audit()
    {
        using var client = CreateClient();
        using var loginResponse = await LoginAsync(client, "planner.demo", "Demo@12345");
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);

        var token = await GetAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["ReturnUrl"] = "/login" })
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var logoutResponse = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, logoutResponse.StatusCode);
        Assert.Equal("/login", logoutResponse.Headers.Location?.OriginalString);
        using var currentUserResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, currentUserResponse.StatusCode);
        Assert.True(await AuditExistsAsync("Logout", "planner.demo"));
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
        BaseAddress = new Uri("https://localhost")
    });

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string username, string password)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Username"] = username,
                ["Password"] = password,
                ["ReturnUrl"] = "/"
            })
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery");
        return Assert.IsType<string>(response?.Token);
    }

    private async Task<bool> AuditExistsAsync(string action, string username)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.AuditLogs.AnyAsync(x => x.Action == action && x.Username == username);
    }

    private sealed record AntiforgeryResponse(string? Token);
}
