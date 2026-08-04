using System.Net;
using System.Net.Http.Json;
using AI.Factory.Core.Domain;
using AI.Factory.Core.MasterData;
using AI.Factory.Core.Orders;
using AI.Factory.Core.Production;
using AI.Factory.Infrastructure.Persistence;
using AI.Factory.Infrastructure.Production;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Factory.IntegrationTests;

public sealed class ProductionPlanTests : IClassFixture<AiFactoryWebApplicationFactory>
{
    private static readonly DateTime TestTime = new(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc);
    private readonly AiFactoryWebApplicationFactory _factory;
    public ProductionPlanTests(AiFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Canonical_plan_seed_is_idempotent_and_preserves_required_demo_calculation()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await db.ProductionPlans.AsNoTracking().SingleAsync(x => x.PlanNumber == "PP-DEMO-001");
        await CanonicalProductionPlanSeeder.SeedAsync(scope.ServiceProvider);
        var after = await db.ProductionPlans.AsNoTracking().SingleAsync(x => x.PlanNumber == "PP-DEMO-001");
        var canonicalNumbers = new[] { "PP-DEMO-001", "PP-0002", "PP-0003", "PP-0004", "PP-0006", "PP-0007", "PP-0008", "PP-0009" };

        Assert.Equal(3, await db.Machines.CountAsync(x => new[] { "Machine-01", "Machine-02", "Machine-03" }.Contains(x.MachineCode)));
        Assert.Equal(8, await db.ProductionPlans.CountAsync(x => canonicalNumbers.Contains(x.PlanNumber)));
        Assert.Equal(before.CreatedAt, after.CreatedAt);
        Assert.Equal(before.PlannedCompletionDate, after.PlannedCompletionDate);
        Assert.Equal(after.CreatedAt.Date.AddDays(5), after.PlannedCompletionDate);
        Assert.Equal(20, after.RequiredBatch);
        var rm001 = await db.MaterialRequirements.Include(x => x.RawMaterial)
            .SingleAsync(x => x.ProductionPlanId == after.Id && x.RawMaterial.Code == "RM-001");
        Assert.Equal(5_000, rm001.RequiredQuantity);
        var service = scope.ServiceProvider.GetRequiredService<IProductionPlanService>();
        var demo = (await service.ListAsync()).Single(x => x.Id == after.Id);
        Assert.Equal(RiskStatus.Critical, demo.RiskStatus);
    }

    [Fact]
    public async Task Planner_creates_plan_required_batch_and_materials_in_one_operation()
    {
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");
        var created = await CreatePlannedOrderAsync(client, 1_001);
        var machine = (await client.GetFromJsonAsync<MachineOptionDto[]>("/api/production-plans/machines"))!.First();
        var planNumber = Unique("PP-CREATE-");
        var payload = new
        {
            planNumber,
            customerOrderId = created.Id,
            machineId = machine.Id,
            plannedCompletionDate = created.DeliveryDate.AddDays(2),
            requiredBatch = 999,
            riskStatus = RiskStatus.Normal
        };

        using var response = await SendJsonAsync(client, HttpMethod.Post, "/api/production-plans", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var plan = await response.Content.ReadFromJsonAsync<ProductionPlanDto>();
        Assert.NotNull(plan);
        Assert.Equal(3, plan.RequiredBatch);
        Assert.Equal(ProductionPlanStatus.Planned, plan.LifecycleStatus);
        Assert.Equal(RiskStatus.Critical, plan.RiskStatus);
        Assert.Equal(3, plan.MaterialRequirements.Count);
        Assert.Contains(plan.MaterialRequirements, x => x.RawMaterialCode == "RM-001" && x.RequiredQuantity == 750);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.ProductionPlans.AnyAsync(x => x.Id == plan.Id));
        Assert.Equal(3, await db.MaterialRequirements.CountAsync(x => x.ProductionPlanId == plan.Id));
    }

    [Fact]
    public async Task Duplicate_plan_for_order_returns_conflict_without_duplicate_requirements()
    {
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");
        var existing = (await client.GetFromJsonAsync<ProductionPlanDto[]>("/api/production-plans"))!.Single(x => x.PlanNumber == "PP-DEMO-001");
        var beforeRequirements = existing.MaterialRequirements.Count;
        using var response = await SendJsonAsync(client, HttpMethod.Post, "/api/production-plans",
            new CreateProductionPlanCommand(Unique("PP-DUP-"), existing.CustomerOrderId, existing.MachineId, existing.PlannedCompletionDate));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.ProductionPlans.CountAsync(x => x.CustomerOrderId == existing.CustomerOrderId));
        Assert.Equal(beforeRequirements, await db.MaterialRequirements.CountAsync(x => x.ProductionPlanId == existing.Id));
    }

    [Fact]
    public async Task Draft_order_cannot_create_plan_and_leaves_no_partial_records()
    {
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");
        var order = await CreateDraftOrderAsync(client, 500);
        var machine = (await client.GetFromJsonAsync<MachineOptionDto[]>("/api/production-plans/machines"))!.First();
        var planNumber = Unique("PP-DRAFT-");
        using var response = await SendJsonAsync(client, HttpMethod.Post, "/api/production-plans",
            new CreateProductionPlanCommand(planNumber, order.Id, machine.Id, TestTime.AddDays(2)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.ProductionPlans.AnyAsync(x => x.PlanNumber == planNumber));
        Assert.False(await db.MaterialRequirements.AnyAsync(x => x.ProductionPlan.PlanNumber == planNumber));
    }

    [Fact]
    public async Task Plan_lifecycle_allows_one_step_and_rejects_skip_or_backwards_transition()
    {
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");
        var plan = await CreatePlanAsync(client, 500);
        using var skipResponse = await SendJsonAsync(client, HttpMethod.Post, $"/api/production-plans/{plan.Id}/lifecycle",
            new TransitionProductionPlanCommand(ProductionPlanStatus.Completed, plan.RowVersion));
        Assert.Equal(HttpStatusCode.BadRequest, skipResponse.StatusCode);
        using var inProductionResponse = await SendJsonAsync(client, HttpMethod.Post, $"/api/production-plans/{plan.Id}/lifecycle",
            new TransitionProductionPlanCommand(ProductionPlanStatus.InProduction, plan.RowVersion));
        Assert.Equal(HttpStatusCode.OK, inProductionResponse.StatusCode);
        var inProduction = await inProductionResponse.Content.ReadFromJsonAsync<ProductionPlanDto>();
        Assert.NotNull(inProduction);

        using var backResponse = await SendJsonAsync(client, HttpMethod.Post, $"/api/production-plans/{plan.Id}/lifecycle",
            new TransitionProductionPlanCommand(ProductionPlanStatus.Planned, inProduction.RowVersion));
        Assert.Equal(HttpStatusCode.BadRequest, backResponse.StatusCode);
    }

    [Fact]
    public async Task Stale_plan_row_version_returns_http_conflict()
    {
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");
        var plan = (await client.GetFromJsonAsync<ProductionPlanDto[]>("/api/production-plans"))!.First(x => x.LifecycleStatus == ProductionPlanStatus.Planned);
        using var response = await SendJsonAsync(client, HttpMethod.Post, $"/api/production-plans/{plan.Id}/lifecycle",
            new TransitionProductionPlanCommand(ProductionPlanStatus.InProduction, [1]));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_can_read_plans_but_cannot_create_them()
    {
        using var client = CreateClient();
        await LoginAsync(client, "viewer.demo");
        using var listResponse = await client.GetAsync("/api/production-plans");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var existing = (await listResponse.Content.ReadFromJsonAsync<ProductionPlanDto[]>())!.First();
        using var createResponse = await SendJsonAsync(client, HttpMethod.Post, "/api/production-plans",
            new CreateProductionPlanCommand("PP-VIEWER-DENIED", existing.CustomerOrderId, existing.MachineId, TestTime));
        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
    }

    private async Task<ProductionPlanDto> CreatePlanAsync(HttpClient client, decimal quantity)
    {
        var order = await CreatePlannedOrderAsync(client, quantity);
        var machine = (await client.GetFromJsonAsync<MachineOptionDto[]>("/api/production-plans/machines"))!.First();
        using var response = await SendJsonAsync(client, HttpMethod.Post, "/api/production-plans",
            new CreateProductionPlanCommand(Unique("PP-FLOW-"), order.Id, machine.Id, order.DeliveryDate.AddDays(-2)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductionPlanDto>())!;
    }

    private async Task<CustomerOrderDto> CreatePlannedOrderAsync(HttpClient client, decimal quantity)
    {
        var draft = await CreateDraftOrderAsync(client, quantity);
        using var response = await SendJsonAsync(client, HttpMethod.Post, $"/api/customer-orders/{draft.Id}/lifecycle",
            new TransitionCustomerOrderCommand(CustomerOrderStatus.Planned, draft.RowVersion));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CustomerOrderDto>())!;
    }

    private async Task<CustomerOrderDto> CreateDraftOrderAsync(HttpClient client, decimal quantity)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var formulationId = await db.Formulations.Where(x => x.Code == "FM-DEMO-001").Select(x => x.Id).SingleAsync();
        using var response = await SendJsonAsync(client, HttpMethod.Post, "/api/customer-orders",
            new CreateCustomerOrderCommand(Unique("SO-PLAN-"), "Planning Customer", formulationId, quantity, TestTime.AddDays(5), "High"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CustomerOrderDto>())!;
    }

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..30];
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
    private static async Task<HttpResponseMessage> SendJsonAsync<T>(HttpClient client, HttpMethod method, string uri, T payload)
    {
        var request = new HttpRequestMessage(method, uri) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-XSRF-TOKEN", await GetTokenAsync(client));
        return await client.SendAsync(request);
    }
    private static async Task<string> GetTokenAsync(HttpClient client) => (await client.GetFromJsonAsync<TokenResponse>("/api/auth/antiforgery"))!.Token;
    private sealed record TokenResponse(string Token);
}
