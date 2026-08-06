using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.Factory.Core.Domain;
using AI.Factory.Core.Machines;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AI.Factory.IntegrationTests;

public sealed class MachineTests : IClassFixture<AiFactoryWebApplicationFactory>
{
    private readonly AiFactoryWebApplicationFactory _factory;
    public MachineTests(AiFactoryWebApplicationFactory factory) => _factory = factory;

    /// <summary>
    /// Machines are a fixed, finite, non-creatable resource shared by every test in this class
    /// (unlike other days' fixtures, there is no "create my own machine" escape hatch), and
    /// sibling tests here legitimately mutate Machine-01/03 via Simulate Update. So this asserts
    /// the property that holds regardless of execution order - AlertStatus always matches the
    /// locked rule for whatever the current reading is - rather than the pristine seed values.
    /// </summary>
    [Fact]
    public async Task List_returns_all_three_machines_with_alert_status_matching_the_locked_rule()
    {
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");

        var machines = (await client.GetFromJsonAsync<MachineDto[]>("/api/machines"))!;

        Assert.Equal(3, machines.Length);
        Assert.All(machines, m => Assert.Equal(MachineRules.CalculateAlertStatus(m.RunningStatus, m.Temperature), m.AlertStatus));
    }

    [Fact]
    public async Task Admin_simulate_recomputes_alert_status_server_side_and_dedups_the_alert_on_repeat()
    {
        using var client = CreateClient();
        await LoginAsync(client, "admin.demo");
        var machine = (await client.GetFromJsonAsync<MachineDto[]>("/api/machines"))!.Single(x => x.MachineCode == "Machine-01");

        using var first = await SendJsonAsync(client, $"/api/machines/{machine.Id}/simulate",
            new SimulateMachineUpdateCommand(MachineRunningStatus.Running, 96, 70, machine.RowVersion));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var updated = (await first.Content.ReadFromJsonAsync<MachineDto>())!;
        Assert.Equal(RiskStatus.Critical, updated.AlertStatus);

        var alertsAfterFirst = await GetActiveAlertCountAsync(client, updated.Id);

        using var second = await SendJsonAsync(client, $"/api/machines/{updated.Id}/simulate",
            new SimulateMachineUpdateCommand(MachineRunningStatus.Running, 97, 70, updated.RowVersion));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var alertsAfterSecond = await GetActiveAlertCountAsync(client, updated.Id);

        Assert.Equal(1, alertsAfterFirst);
        Assert.Equal(1, alertsAfterSecond);
    }

    [Fact]
    public async Task Resolving_the_condition_clears_the_active_alert()
    {
        using var client = CreateClient();
        await LoginAsync(client, "admin.demo");
        var machine = (await client.GetFromJsonAsync<MachineDto[]>("/api/machines"))!.Single(x => x.MachineCode == "Machine-03");

        // Machine-03 seeds Stopped/35°C = Warning; reading alerts evaluates and must surface it.
        Assert.True(await HasActiveMachineAlertAsync(client, machine.Id), "Machine-03's seeded Warning alert should exist before it's resolved.");

        using var response = await SendJsonAsync(client, $"/api/machines/{machine.Id}/simulate",
            new SimulateMachineUpdateCommand(MachineRunningStatus.Running, 60, 80, machine.RowVersion));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<MachineDto>())!;
        Assert.Equal(RiskStatus.Normal, updated.AlertStatus);

        Assert.False(await HasActiveMachineAlertAsync(client, updated.Id), "The alert must clear once the machine returns to Normal.");
    }

    [Fact]
    public async Task Only_admin_can_simulate_but_all_roles_can_list()
    {
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");
        var machine = (await client.GetFromJsonAsync<MachineDto[]>("/api/machines"))!.First();

        using var list = await client.GetAsync("/api/machines");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        using var simulate = await SendJsonAsync(client, $"/api/machines/{machine.Id}/simulate",
            new SimulateMachineUpdateCommand(machine.RunningStatus, machine.Temperature, machine.Speed, machine.RowVersion));
        Assert.Equal(HttpStatusCode.Forbidden, simulate.StatusCode);
    }

    [Fact]
    public async Task Negative_temperature_or_speed_is_rejected()
    {
        using var client = CreateClient();
        await LoginAsync(client, "admin.demo");
        var machine = (await client.GetFromJsonAsync<MachineDto[]>("/api/machines"))!.First();

        using var response = await SendJsonAsync(client, $"/api/machines/{machine.Id}/simulate",
            new SimulateMachineUpdateCommand(MachineRunningStatus.Running, -1, 10, machine.RowVersion));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_wrong_row_version_returns_conflict()
    {
        using var client = CreateClient();
        await LoginAsync(client, "admin.demo");
        var machine = (await client.GetFromJsonAsync<MachineDto[]>("/api/machines"))!.First();

        using var response = await SendJsonAsync(client, $"/api/machines/{machine.Id}/simulate",
            new SimulateMachineUpdateCommand(machine.RunningStatus, machine.Temperature, machine.Speed, [1]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_machine_read_is_rejected()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/api/machines");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<int> GetActiveAlertCountAsync(HttpClient client, long machineId)
    {
        var alerts = await client.GetFromJsonAsync<JsonElement[]>("/api/dashboard/alerts");
        return alerts!.Count(a => a.GetProperty("entityName").GetString() == "Machine" && a.GetProperty("entityId").GetInt64() == machineId);
    }

    private static async Task<bool> HasActiveMachineAlertAsync(HttpClient client, long machineId) =>
        await GetActiveAlertCountAsync(client, machineId) > 0;

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
