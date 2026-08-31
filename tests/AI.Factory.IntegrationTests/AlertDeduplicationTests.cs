using System.Net;
using System.Net.Http.Json;
using AI.Factory.Core.Domain;
using AI.Factory.Core.Machines;
using AI.Factory.Core.Reporting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AI.Factory.IntegrationTests;

/// <summary>
/// Its own class, and therefore its own fixture instance: these tests drive a machine through
/// several states, which would otherwise disturb siblings sharing a database.
/// </summary>
public sealed class AlertDeduplicationTests : IClassFixture<AiFactoryWebApplicationFactory>
{
    private readonly AiFactoryWebApplicationFactory _factory;
    public AlertDeduplicationTests(AiFactoryWebApplicationFactory factory) => _factory = factory;

    /// <summary>
    /// Deduplication updates an existing active alert in place rather than raising a second one -
    /// that part was right. What it did not update was Severity: only Message was assigned, so a
    /// machine climbing from 86 to 96 degrees kept the same dedup key and kept its old Warning
    /// severity while its own message changed to read "(Critical)".
    ///
    /// That contradiction is visible on one row of the Dashboard, and worse than cosmetic: the alert
    /// list sorts Critical first, so a genuinely critical machine sorted below the real Critical
    /// alerts on the screen whose entire job is to surface the worst thing happening.
    ///
    /// Reproduced live on LocalDB before the fix - severity=Warning against
    /// "Machine-02 is running at 96.0C (Critical)".
    /// </summary>
    [Fact]
    public async Task Severity_follows_the_condition_when_an_existing_alert_is_updated_in_place()
    {
        using var client = CreateClient();
        await LoginAsync(client, "admin.demo");
        var machineId = (await MachinesAsync(client)).First(x => x.MachineCode == "Machine-02").Id;

        // Clear whatever the seed left, so the next step raises a genuinely new alert.
        await SimulateAsync(client, machineId, 70m);
        Assert.DoesNotContain(await MachineAlertsAsync(client), x => x.EntityId == machineId);

        // 86C is Warning per the locked MachineRules boundaries.
        await SimulateAsync(client, machineId, 86m);
        var warning = Assert.Single(await MachineAlertsAsync(client), x => x.EntityId == machineId);
        Assert.Equal(AlertType.MachineTemperature, warning.AlertType);
        Assert.Equal(AlertSeverity.Warning, warning.Severity);

        // 96C is Critical. Same dedup key, so this is the in-place update path.
        await SimulateAsync(client, machineId, 96m);
        var critical = Assert.Single(await MachineAlertsAsync(client), x => x.EntityId == machineId);
        Assert.Equal(warning.Id, critical.Id);
        Assert.Equal(AlertSeverity.Critical, critical.Severity);
        Assert.Contains("96.0", critical.Message);

        // And back down again: the reverse direction was equally stuck, over-reporting a cleared
        // condition as Critical.
        await SimulateAsync(client, machineId, 86m);
        var backToWarning = Assert.Single(await MachineAlertsAsync(client), x => x.EntityId == machineId);
        Assert.Equal(AlertSeverity.Warning, backToWarning.Severity);
    }

    private static async Task<MachineDto[]> MachinesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<MachineDto[]>("/api/machines"))!;

    private static async Task<ActiveAlertDto[]> MachineAlertsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ActiveAlertDto[]>("/api/dashboard/alerts"))!
            .Where(x => x.EntityName == "Machine").ToArray();

    private static async Task SimulateAsync(HttpClient client, long machineId, decimal temperature)
    {
        var machine = (await MachinesAsync(client)).Single(x => x.Id == machineId);
        using var response = await SendJsonAsync(client, $"/api/machines/{machineId}/simulate",
            new SimulateMachineUpdateCommand(MachineRunningStatus.Running, temperature, 60m, machine.RowVersion));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });

    private static async Task<HttpResponseMessage> SendJsonAsync<T>(HttpClient client, string uri, T payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-XSRF-TOKEN", await GetTokenAsync(client));
        return await client.SendAsync(request);
    }

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
