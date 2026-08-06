using System.Net;
using System.Net.Http.Json;
using System.Text;
using AI.Factory.Core.Audit;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AI.Factory.IntegrationTests;

public sealed class AuditLogTests : IClassFixture<AiFactoryWebApplicationFactory>
{
    private static readonly byte[] Utf8Bom = Encoding.UTF8.GetPreamble();
    private readonly AiFactoryWebApplicationFactory _factory;
    public AuditLogTests(AiFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task List_filter_search_and_paging_reflect_actions_already_taken_in_this_run()
    {
        using var client = CreateClient();
        await LoginAsync(client, "admin.demo"); // itself writes a "Login Success" AuditLog row

        var all = await client.GetFromJsonAsync<AuditLogPage>("/api/audit-logs?page=1&pageSize=100");
        Assert.NotEmpty(all!.Items);
        Assert.Contains(all.Items, x => x.Action == "Login Success" && x.Username == "admin.demo");

        var byAction = await client.GetFromJsonAsync<AuditLogPage>("/api/audit-logs?action=Login%20Success&page=1&pageSize=100");
        Assert.All(byAction!.Items, x => Assert.Equal("Login Success", x.Action));

        var bySearch = await client.GetFromJsonAsync<AuditLogPage>("/api/audit-logs?search=admin.demo&page=1&pageSize=100");
        Assert.All(bySearch!.Items, x => Assert.Contains("admin.demo", x.Username, StringComparison.OrdinalIgnoreCase));

        var firstPage = await client.GetFromJsonAsync<AuditLogPage>("/api/audit-logs?page=1&pageSize=1");
        Assert.Single(firstPage!.Items);
        Assert.Equal(1, firstPage.Page);
    }

    [Theory]
    [InlineData("admin.demo", HttpStatusCode.OK)]
    [InlineData("manager.demo", HttpStatusCode.OK)]
    [InlineData("planner.demo", HttpStatusCode.Forbidden)]
    [InlineData("viewer.demo", HttpStatusCode.Forbidden)]
    public async Task Only_admin_and_manager_can_view_the_audit_log(string username, HttpStatusCode expected)
    {
        using var client = CreateClient();
        await LoginAsync(client, username);

        using var response = await client.GetAsync("/api/audit-logs");

        Assert.Equal(expected, response.StatusCode);
    }

    /// <summary>
    /// vw_AuditLogReport is a real SQL Server VIEW; the InMemory provider used here has no SQL
    /// engine to execute it (same structural gap documented for Day 9's report views), so this
    /// checks endpoint wiring and role gating only. Row content is verified live against LocalDB.
    /// </summary>
    [Theory]
    [InlineData("admin.demo", HttpStatusCode.OK)]
    [InlineData("manager.demo", HttpStatusCode.OK)]
    [InlineData("planner.demo", HttpStatusCode.Forbidden)]
    public async Task Audit_log_csv_export_is_restricted_to_who_can_view_it(string username, HttpStatusCode expected)
    {
        using var client = CreateClient();
        await LoginAsync(client, username);

        using var response = await client.GetAsync("/api/reports/audit-log/export.csv");

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            Assert.StartsWith("text/csv", response.Content.Headers.ContentType!.ToString());
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal(Utf8Bom, bytes[..Utf8Bom.Length]);
            var text = Encoding.UTF8.GetString(bytes, Utf8Bom.Length, bytes.Length - Utf8Bom.Length);
            Assert.StartsWith("Username,Action,Entity,Entity Id,Result,Request Id,Created At\r\n", text);
        }
    }

    [Fact]
    public async Task Anonymous_audit_log_read_is_rejected()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/api/audit-logs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
