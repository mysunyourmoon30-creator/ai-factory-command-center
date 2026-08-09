using System.Net;
using System.Net.Http.Json;
using AI.Factory.Core.Domain;
using AI.Factory.Core.Production;
using AI.Factory.Core.Reporting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Factory.IntegrationTests;

/// <summary>
/// Ordinary tests, not additions to the locked 15 Required business tests - the Executive Overview
/// screen restates existing calculations and introduces no business rule of its own. The cross-check
/// tests below are the point of this file: screen 12 must never be able to disagree with screen 1.
/// </summary>
public sealed class ExecutiveTests : IClassFixture<AiFactoryWebApplicationFactory>
{
    private readonly AiFactoryWebApplicationFactory _factory;
    public ExecutiveTests(AiFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Order_pipeline_totals_agree_with_the_dashboard_customer_order_count()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var kpi = await scope.ServiceProvider.GetRequiredService<IDashboardService>().GetKpiAsync();
        var overview = await scope.ServiceProvider.GetRequiredService<IExecutiveService>().GetOverviewAsync();

        Assert.Equal(kpi.CustomerOrderCount, overview.Pipeline.Total);
        Assert.Equal(10, overview.Pipeline.Total);
    }

    /// <summary>
    /// AtRisk is defined as Warning + Critical precisely so it reproduces the dashboard's single
    /// "Orders at Risk" KPI. If this ever fails, the two screens have drifted apart.
    /// </summary>
    [Fact]
    public async Task Delivery_risk_breakdown_sums_to_the_dashboard_orders_at_risk_kpi()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var kpi = await scope.ServiceProvider.GetRequiredService<IDashboardService>().GetKpiAsync();
        var overview = await scope.ServiceProvider.GetRequiredService<IExecutiveService>().GetOverviewAsync();

        Assert.Equal(kpi.OrdersAtRiskCount, overview.DeliveryRisk.AtRisk);
        Assert.Equal(kpi.CustomerOrderCount, overview.DeliveryRisk.Total);
        Assert.True(overview.DeliveryRisk.Critical >= 1, "SO-DEMO-001 must be Critical in the canonical seed.");
    }

    [Fact]
    public async Task Procurement_funnel_matches_the_canonical_seed_and_the_dashboard_late_count()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var kpi = await scope.ServiceProvider.GetRequiredService<IDashboardService>().GetKpiAsync();
        var overview = await scope.ServiceProvider.GetRequiredService<IExecutiveService>().GetOverviewAsync();
        var funnel = overview.Procurement;

        Assert.Equal(kpi.LatePurchaseOrderCount, funnel.PurchaseOrdersLate);

        var procurement = scope.ServiceProvider.GetRequiredService<IProcurementService>();
        var requests = await procurement.ListPurchaseRequestsAsync();
        Assert.Equal(requests.Count(x => x.Status == PurchaseRequestStatus.Draft), funnel.RequestsDraft);
        Assert.Equal(requests.Count(x => x.Status == PurchaseRequestStatus.PendingApproval), funnel.RequestsPendingApproval);
        Assert.Equal(requests.Count(x => x.Status == PurchaseRequestStatus.Approved), funnel.RequestsApproved);
        Assert.Equal(requests.Count(x => x.Status == PurchaseRequestStatus.Rejected), funnel.RequestsRejected);

        var purchaseOrders = await procurement.ListIncomingPurchaseOrdersAsync();
        Assert.Equal(purchaseOrders.Count(x => x.Status == IncomingPurchaseOrderStatus.Open), funnel.PurchaseOrdersOpen);
        Assert.Equal(purchaseOrders.Count(x => x.Status == IncomingPurchaseOrderStatus.Partial), funnel.PurchaseOrdersPartial);
        Assert.Equal(purchaseOrders.Count(x => x.Status == IncomingPurchaseOrderStatus.Received), funnel.PurchaseOrdersReceived);
    }

    /// <summary>
    /// Ordering and Take only. The shortage figures themselves come from the locked cumulative rules
    /// via IMaterialRequirementQueryService and are asserted to be the same values /material-shortages
    /// shows, never recomputed here.
    /// </summary>
    [Fact]
    public async Task Top_shortages_are_the_worst_rows_from_the_same_shortage_engine()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var overview = await scope.ServiceProvider.GetRequiredService<IExecutiveService>().GetOverviewAsync();
        var materials = await scope.ServiceProvider.GetRequiredService<IMaterialRequirementQueryService>().ListAvailabilityAsync();

        var expected = materials
            .Where(x => x.ShortageQuantity > 0)
            .OrderByDescending(x => x.ShortageQuantity)
            .ThenBy(x => x.RawMaterialCode, StringComparer.Ordinal)
            .Take(IExecutiveService.TopShortageCount)
            .ToArray();

        Assert.Equal(expected.Length, overview.TopShortages.Count);
        Assert.Equal(expected.Select(x => x.RawMaterialCode), overview.TopShortages.Select(x => x.RawMaterialCode));
        Assert.All(overview.TopShortages, x => Assert.True(x.ShortageQuantity > 0, "Only rows with a real shortage belong on this list."));
        Assert.True(overview.TopShortages.Count <= IExecutiveService.TopShortageCount);

        var worst = overview.TopShortages.First();
        Assert.Equal("RM-001", worst.RawMaterialCode);
        Assert.Equal(1250m, worst.ShortageQuantity);
    }

    [Fact]
    public async Task Machine_availability_lists_the_three_locked_machines_in_code_order()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var overview = await scope.ServiceProvider.GetRequiredService<IExecutiveService>().GetOverviewAsync();

        Assert.Equal(["Machine-01", "Machine-02", "Machine-03"], overview.Machines.Select(x => x.MachineCode));
        Assert.Contains(overview.Machines, x => x.MachineCode == "Machine-02" && x.AlertStatus == RiskStatus.Critical);
    }

    [Fact]
    public async Task As_of_date_comes_from_the_fixed_time_provider()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var overview = await scope.ServiceProvider.GetRequiredService<IExecutiveService>().GetOverviewAsync();

        Assert.Equal(scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime.Date, overview.AsOfDate);
    }

    /// <summary>
    /// Every figure on this screen is already readable by every role elsewhere, so the endpoint
    /// carries a bare RequireAuthorization like the other read groups. The nav link is hidden from
    /// Planner and Viewer for relevance, which is presentation and deliberately not a gate here.
    /// </summary>
    [Theory]
    [InlineData("admin.demo")]
    [InlineData("manager.demo")]
    [InlineData("planner.demo")]
    [InlineData("viewer.demo")]
    public async Task Every_authenticated_role_can_read_the_executive_overview(string username)
    {
        using var client = CreateClient();
        await LoginAsync(client, username);

        using var response = await client.GetAsync("/api/executive/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_executive_read_is_rejected()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/api/executive/overview");

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
