using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using AI.Factory.Core.Security;
using AI.Factory.Core.Domain;
using AI.Factory.Core.MasterData;
using AI.Factory.Core.Orders;
using AI.Factory.Infrastructure.Orders;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Factory.IntegrationTests;

public sealed class CustomerOrderTests : IClassFixture<AiFactoryWebApplicationFactory>
{
    private static readonly DateTime TestTime = new(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc);
    private readonly AiFactoryWebApplicationFactory _factory;
    public CustomerOrderTests(AiFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Canonical_order_seed_is_idempotent_and_preserves_anchor_dates()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await db.CustomerOrders.AsNoTracking().SingleAsync(x => x.OrderNumber == "SO-DEMO-001");
        await CanonicalCustomerOrderSeeder.SeedAsync(scope.ServiceProvider);
        var after = await db.CustomerOrders.AsNoTracking().SingleAsync(x => x.OrderNumber == "SO-DEMO-001");
        var canonicalNumbers = new[] { "SO-DEMO-001", "SO-0002", "SO-0003", "SO-0004", "SO-0005", "SO-0006", "SO-0007", "SO-0008", "SO-0009", "SO-0010" };

        Assert.Equal(10, await db.CustomerOrders.CountAsync(x => canonicalNumbers.Contains(x.OrderNumber)));
        Assert.Equal(before.CreatedAt, after.CreatedAt);
        Assert.Equal(before.DeliveryDate, after.DeliveryDate);
        Assert.Equal(after.CreatedAt.Date.AddDays(3), after.DeliveryDate);
    }

    [Fact]
    public async Task Planner_can_create_and_update_draft_but_payload_cannot_change_status_or_risk()
    {
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");
        var formulation = await FirstFormulationAsync();
        var number = $"SO-TEST-{Guid.NewGuid():N}"[..30];
        using var createResponse = await SendJsonAsync(client, HttpMethod.Post, "/api/customer-orders",
            new CreateCustomerOrderCommand(number, "Test Customer", formulation.Id, 125, TestTime.AddDays(5), "High"));
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CustomerOrderDto>();
        Assert.NotNull(created);
        Assert.Equal(CustomerOrderStatus.Draft, created.LifecycleStatus);
        Assert.Equal(RiskStatus.Normal, created.RiskStatus);

        var payload = new
        {
            orderNumber = created.OrderNumber,
            customerName = "Updated Customer",
            formulationId = created.FormulationId,
            quantity = 250,
            deliveryDate = created.DeliveryDate.AddDays(1),
            priority = "Urgent",
            rowVersion = created.RowVersion,
            lifecycleStatus = CustomerOrderStatus.Completed,
            riskStatus = RiskStatus.Critical
        };
        using var updateResponse = await SendJsonAsync(client, HttpMethod.Put, $"/api/customer-orders/{created.Id}", payload);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<CustomerOrderDto>();
        Assert.NotNull(updated);
        Assert.Equal("Updated Customer", updated.CustomerName);
        Assert.Equal(250, updated.Quantity);
        Assert.Equal(CustomerOrderStatus.Draft, updated.LifecycleStatus);
        Assert.Equal(RiskStatus.Normal, updated.RiskStatus);
    }

    [Fact]
    public async Task Non_draft_and_planned_orders_cannot_use_general_update()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ICustomerOrderService>();
        var planned = (await service.ListAsync(new("SO-0002"))).Items.Single(x => x.OrderNumber == "SO-0002");
        var command = new UpdateCustomerOrderCommand(planned.OrderNumber, planned.CustomerName, planned.FormulationId, planned.Quantity + 1, planned.DeliveryDate, planned.Priority, planned.RowVersion);
        var error = await Assert.ThrowsAsync<DomainValidationException>(() => service.UpdateAsync(planned.Id, command, PlannerActor()));
        Assert.Contains("Draft", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Production_plan_permanently_locks_draft_quantity_and_formulation()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var formulation = await db.Formulations.FirstAsync();
        var order = new CustomerOrder
        {
            OrderNumber = $"SO-LOCK-{Guid.NewGuid():N}"[..30],
            CustomerName = "Locked Customer",
            FormulationId = formulation.Id,
            Quantity = 100,
            DeliveryDate = TestTime.AddDays(10),
            Priority = "Normal",
            Status = CustomerOrderStatus.Draft,
            CreatedByUserId = 1,
            CreatedAt = TestTime,
            UpdatedAt = TestTime
        };
        db.CustomerOrders.Add(order);
        await db.SaveChangesAsync();
        db.ProductionPlans.Add(new ProductionPlan
        {
            PlanNumber = $"PP-LOCK-{Guid.NewGuid():N}"[..30],
            CustomerOrderId = order.Id,
            MachineId = 1,
            RequiredBatch = 1,
            PlannedCompletionDate = order.DeliveryDate.AddDays(-2),
            Status = ProductionPlanStatus.Planned,
            CreatedByUserId = 1,
            CreatedAt = TestTime
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = scope.ServiceProvider.GetRequiredService<ICustomerOrderService>();
        var dto = await service.GetAsync(order.Id);
        Assert.NotNull(dto);
        var command = new UpdateCustomerOrderCommand(dto.OrderNumber, dto.CustomerName, dto.FormulationId, dto.Quantity + 1, dto.DeliveryDate, dto.Priority, dto.RowVersion);
        await Assert.ThrowsAsync<DomainValidationException>(() => service.UpdateAsync(dto.Id, command, PlannerActor()));
    }

    [Fact]
    public async Task Lifecycle_allows_one_step_and_rejects_skips_or_backwards_moves()
    {
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");
        var formulation = await FirstFormulationAsync();
        var number = $"SO-FLOW-{Guid.NewGuid():N}"[..30];
        using var createdResponse = await SendJsonAsync(client, HttpMethod.Post, "/api/customer-orders",
            new CreateCustomerOrderCommand(number, "Flow Customer", formulation.Id, 100, TestTime.AddDays(5), "Normal"));
        var created = await createdResponse.Content.ReadFromJsonAsync<CustomerOrderDto>();
        Assert.NotNull(created);

        using var plannedResponse = await SendJsonAsync(client, HttpMethod.Post, $"/api/customer-orders/{created.Id}/lifecycle",
            new TransitionCustomerOrderCommand(CustomerOrderStatus.Planned, created.RowVersion));
        Assert.Equal(HttpStatusCode.OK, plannedResponse.StatusCode);
        var planned = await plannedResponse.Content.ReadFromJsonAsync<CustomerOrderDto>();
        Assert.NotNull(planned);
        Assert.Equal(CustomerOrderStatus.Planned, planned.LifecycleStatus);

        using var skipResponse = await SendJsonAsync(client, HttpMethod.Post, $"/api/customer-orders/{planned.Id}/lifecycle",
            new TransitionCustomerOrderCommand(CustomerOrderStatus.Completed, planned.RowVersion));
        Assert.Equal(HttpStatusCode.BadRequest, skipResponse.StatusCode);
        using var backResponse = await SendJsonAsync(client, HttpMethod.Post, $"/api/customer-orders/{planned.Id}/lifecycle",
            new TransitionCustomerOrderCommand(CustomerOrderStatus.Draft, planned.RowVersion));
        Assert.Equal(HttpStatusCode.BadRequest, backResponse.StatusCode);
    }

    [Fact]
    public async Task Stale_order_row_version_returns_http_conflict()
    {
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");
        CustomerOrderDto draft;
        using (var response = await client.GetAsync("/api/customer-orders?search=SO-0010"))
        {
            var page = await response.Content.ReadFromJsonAsync<CustomerOrderPage>();
            draft = Assert.Single(page!.Items, x => x.OrderNumber == "SO-0010");
        }
        using var updateResponse = await SendJsonAsync(client, HttpMethod.Put, $"/api/customer-orders/{draft.Id}",
            new UpdateCustomerOrderCommand(draft.OrderNumber, draft.CustomerName, draft.FormulationId, draft.Quantity, draft.DeliveryDate, draft.Priority, [1]));
        Assert.Equal(HttpStatusCode.Conflict, updateResponse.StatusCode);
    }

    [Fact]
    public async Task Viewer_can_read_orders_but_cannot_create_them()
    {
        using var client = CreateClient();
        await LoginAsync(client, "viewer.demo");
        using var listResponse = await client.GetAsync("/api/customer-orders");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var formulation = await FirstFormulationAsync();
        using var createResponse = await SendJsonAsync(client, HttpMethod.Post, "/api/customer-orders",
            new CreateCustomerOrderCommand("SO-VIEWER-DENIED", "Denied", formulation.Id, 1, TestTime.AddDays(1), "Normal"));
        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
    }

    private async Task<FormulationDto> FirstFormulationAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IMasterDataService>();
        return (await service.ListFormulationsAsync()).First();
    }

    private static ClaimsPrincipal PlannerActor() => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, RoleNames.Planner)], "Test"));

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
