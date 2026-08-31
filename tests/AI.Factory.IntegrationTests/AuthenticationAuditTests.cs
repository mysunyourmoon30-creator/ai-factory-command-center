using System.Net;
using System.Net.Http.Json;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Factory.IntegrationTests;

/// <summary>
/// Its own class, and so its own host: locking an account out costs six of the login rate limiter's
/// ten permits per minute, which would starve sibling tests sharing the budget.
/// </summary>
public sealed class AuthenticationAuditTests : IClassFixture<AiFactoryWebApplicationFactory>
{
    private readonly AiFactoryWebApplicationFactory _factory;
    public AuthenticationAuditTests(AiFactoryWebApplicationFactory factory) => _factory = factory;

    /// <summary>
    /// Every failed sign-in was recorded as "Invalid credentials", so the audit could not distinguish
    /// a typo from a lockout, a deactivated account, or an unknown username.
    ///
    /// Reproduced live on LocalDB before the fix: six wrong passwords followed by the *correct* one
    /// produced seven identical rows. The seventh is the alarming one - somebody holding the right
    /// password, refused only because the account had just locked - and it was indistinguishable from
    /// the typos above it on the screen whose whole purpose is traceability.
    ///
    /// Only the audit changes. Every one of these still returns the same redirect to the caller, so no
    /// username-enumeration signal is added, and the audit log is readable only by Admin and Manager.
    /// </summary>
    [Fact]
    public async Task Audit_records_why_a_sign_in_failed_not_just_that_it_did()
    {
        using var client = CreateClient();

        await AttemptAsync(client, "no.such.user", "Demo@12345");

        // Identity's default is five attempts before the account locks.
        for (var i = 0; i < 5; i++)
        {
            await AttemptAsync(client, "viewer.demo", "WrongPassword!");
        }

        // The correct password, refused because the account is now locked.
        await AttemptAsync(client, "viewer.demo", "Demo@12345");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unknown = await db.AuditLogs.Where(x => x.Username == "no.such.user").Select(x => x.Result).ToListAsync();
        Assert.Equal(["Unknown username"], unknown);

        var viewer = await db.AuditLogs.Where(x => x.Username == "viewer.demo" && x.Action == "Login Failure")
            .OrderBy(x => x.Id).Select(x => x.Result).ToListAsync();
        Assert.Equal(6, viewer.Count);
        Assert.Equal("Invalid credentials", viewer[0]);
        Assert.Contains("Locked out", viewer);

        // The last attempt carried the right password and was still refused - the log has to say so.
        Assert.Equal("Locked out", viewer[^1]);
    }

    private static async Task AttemptAsync(HttpClient client, string username, string password)
    {
        var token = (await client.GetFromJsonAsync<TokenResponse>("/api/auth/antiforgery"))!.Token;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Username"] = username, ["Password"] = password, ["ReturnUrl"] = "/" })
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        // Identical to the caller in every case, successful or not.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });

    private sealed record TokenResponse(string Token);
}
