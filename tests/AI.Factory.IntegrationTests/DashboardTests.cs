using System.Net;
using System.Net.Http.Json;
using AI.Factory.Core.Domain;
using AI.Factory.Core.MasterData;
using AI.Factory.Core.Orders;
using AI.Factory.Core.Production;
using AI.Factory.Core.Reporting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Factory.IntegrationTests;

public sealed class DashboardTests : IClassFixture<AiFactoryWebApplicationFactory>
{
    private readonly AiFactoryWebApplicationFactory _factory;
    public DashboardTests(AiFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Kpi_counts_match_the_canonical_seed()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dashboard = scope.ServiceProvider.GetRequiredService<IDashboardService>();
        var kpi = await dashboard.GetKpiAsync();

        Assert.Equal(10, kpi.CustomerOrderCount);
        Assert.Equal(8, kpi.ProductionPlanCount);
        Assert.Equal(0, kpi.LatePurchaseOrderCount);
        Assert.Equal(2, kpi.MachineAlertCount);

        var shortageService = scope.ServiceProvider.GetRequiredService<IMaterialShortageService>();
        var expectedShortageCount = (await shortageService.ListAsync()).Count(x => x.ShortageQuantity > 0);
        Assert.Equal(expectedShortageCount, kpi.MaterialShortageCount);
        Assert.True(kpi.MaterialShortageCount >= 1, "RM-001 must be short in the canonical seed.");

        var orderService = scope.ServiceProvider.GetRequiredService<ICustomerOrderService>();
        var expectedAtRisk = (await orderService.ListAsync(new CustomerOrderQuery(PageSize: 100))).Items.Count(x => x.RiskStatus != RiskStatus.Normal);
        Assert.Equal(expectedAtRisk, kpi.OrdersAtRiskCount);
        Assert.True(kpi.OrdersAtRiskCount >= 1, "SO-DEMO-001 must be at risk in the canonical seed.");
    }

    [Fact]
    public async Task Daily_summary_matches_the_locked_get_daily_factory_summary_shape()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dashboard = scope.ServiceProvider.GetRequiredService<IDashboardService>();
        var kpi = await dashboard.GetKpiAsync();
        var summary = await dashboard.GetDailySummaryAsync();

        Assert.Equal(kpi.CustomerOrderCount, summary.CustomerOrderCount);
        Assert.Equal(kpi.ProductionPlanCount, summary.ProductionPlanCount);
        Assert.Equal(kpi.MaterialShortageCount, summary.MaterialShortageCount);
        Assert.Equal(kpi.LatePurchaseOrderCount, summary.LatePurchaseOrderCount);
        Assert.Equal(1, summary.CriticalMachineCount);
    }

    [Fact]
    public async Task Critical_risks_include_the_locked_demo_order_and_machine()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dashboard = scope.ServiceProvider.GetRequiredService<IDashboardService>();

        var risks = await dashboard.GetCriticalRisksAsync();

        var demoOrder = Assert.Single(risks.DelayedOrders, x => x.OrderNumber == "SO-DEMO-001");
        Assert.Equal(2, demoOrder.DelayRiskDays);
        Assert.Contains(risks.CriticalMachines, x => x.MachineCode == "Machine-02");
    }

    /// <summary>
    /// Day 11 wires alert evaluation into this read, so it's no longer empty against the
    /// canonical seed - superseding the Day 9 placeholder that asserted emptiness only because
    /// nothing evaluated alerts yet.
    /// </summary>
    [Fact]
    public async Task Active_alerts_reflect_the_canonical_seeds_known_conditions()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dashboard = scope.ServiceProvider.GetRequiredService<IDashboardService>();

        var alerts = await dashboard.ListActiveAlertsAsync();

        Assert.Contains(alerts, x => x.AlertType == AlertType.MaterialShortage && x.EntityName == "RawMaterial");
        Assert.Contains(alerts, x => x.AlertType == AlertType.LateProduction && x.EntityName == "CustomerOrder");
        Assert.Contains(alerts, x => x.AlertType == AlertType.MachineTemperature && x.EntityName == "Machine" && x.Severity == AlertSeverity.Critical);
    }

    [Theory]
    [InlineData("admin.demo")]
    [InlineData("manager.demo")]
    [InlineData("planner.demo")]
    [InlineData("viewer.demo")]
    public async Task Every_authenticated_role_can_read_the_dashboard(string username)
    {
        using var client = CreateClient();
        await LoginAsync(client, username);

        using var kpi = await client.GetAsync("/api/dashboard/kpi");
        using var summary = await client.GetAsync("/api/dashboard/daily-summary");
        using var risks = await client.GetAsync("/api/dashboard/critical-risks");
        using var alerts = await client.GetAsync("/api/dashboard/alerts");

        Assert.Equal(HttpStatusCode.OK, kpi.StatusCode);
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        Assert.Equal(HttpStatusCode.OK, risks.StatusCode);
        Assert.Equal(HttpStatusCode.OK, alerts.StatusCode);
    }

    [Fact]
    public async Task Anonymous_dashboard_read_is_rejected()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/api/dashboard/kpi");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });

    private static async Task LoginAsync(HttpClient client, string username)
    {
        var token = (await client.GetFromJsonAsync<TokenResponse>("/api/auth/antiforgery"))!.Token;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Username"] = username, ["Password"] = "Demo@12345", ["ReturnUrl"] = "/" })
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private sealed record TokenResponse(string Token);
}
