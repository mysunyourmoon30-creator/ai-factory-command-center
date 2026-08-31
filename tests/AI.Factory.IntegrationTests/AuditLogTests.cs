using System.Net;
using System.Net.Http.Json;
using System.Text;
using AI.Factory.Core.Audit;
using AI.Factory.Core.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>
    /// Paging must be a partition of the table: every row exactly once, nothing repeated, nothing
    /// lost. It was not. ListAsync sorted on CreatedAt alone, and CreatedAt is datetime2(0), so rows
    /// written in the same second tie; under the fixed clock used here every row ties. With no unique
    /// final sort key the order among tied rows is undefined and OFFSET/FETCH then slices it.
    /// Reproduced on LocalDB before the fix at page size 10: Id 17 came back on both pages and Id 18
    /// on neither.
    ///
    /// Rows are minted through IAuditWriter rather than by logging in repeatedly. Logins would work -
    /// each writes one row - but they spend the shared login rate-limit budget and the later tests in
    /// this class then get a 503 instead of a redirect. Found the hard way.
    ///
    /// The InMemory provider has no optimiser, so it cannot reproduce the plan-dependent ordering
    /// that makes this dangerous in production. It catches the defect a different way: LINQ-to-Objects
    /// sorts stably, so without the tiebreaker the tied rows come back oldest-first and the strict
    /// descending-Id assertion below fails - which is also the plainest statement of the bug, since a
    /// log that advertises newest-first was returning oldest-first whenever the clock was fixed.
    /// </summary>
    [Fact]
    public async Task Paging_visits_every_entry_exactly_once_even_though_all_timestamps_tie()
    {
        const string ProbeAction = "Paging Probe";
        const int PageSize = 3;
        const int RowCount = 10;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IAuditWriter>();
            for (var i = 0; i < RowCount; i++)
            {
                // All ten share the fixed clock's instant, so this is one guaranteed tie group
                // several pages deep.
                await writer.WriteAsync(ProbeAction, "Endpoint", null, $"row {i}", username: "paging.probe");
            }
        }

        using var client = CreateClient();
        await LoginAsync(client, "admin.demo");

        var seen = new List<long>();
        var pageCount = (int)Math.Ceiling(RowCount / (double)PageSize);
        for (var page = 1; page <= pageCount; page++)
        {
            var slice = await client.GetFromJsonAsync<AuditLogPage>(
                $"/api/audit-logs?action={Uri.EscapeDataString(ProbeAction)}&page={page}&pageSize={PageSize}");
            Assert.Equal(RowCount, slice!.TotalCount);
            seen.AddRange(slice.Items.Select(x => x.Id));
        }

        var duplicated = seen.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        Assert.True(duplicated.Length == 0, $"Returned on more than one page: {string.Join(", ", duplicated)}");
        Assert.Equal(RowCount, seen.Distinct().Count());
        Assert.Equal(seen.OrderByDescending(x => x).ToArray(), seen);
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
            Assert.StartsWith("Username,Action,Entity,Entity Id,Result,Request Id,IP Address,User Agent,Created At\r\n", text);
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
