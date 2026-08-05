using System.Net;
using System.Net.Http.Json;
using AI.Factory.Core.Domain;
using AI.Factory.Core.Production;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Factory.IntegrationTests;

public sealed class MaterialShortageTests : IClassFixture<AiFactoryWebApplicationFactory>
{
    private static readonly DateTime TestTime = new(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc);
    private readonly AiFactoryWebApplicationFactory _factory;
    public MaterialShortageTests(AiFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Demo_material_reports_the_locked_shortage_of_one_thousand_two_hundred_fifty()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var plan = await db.ProductionPlans.AsNoTracking().SingleAsync(x => x.PlanNumber == "PP-DEMO-001");

        var service = scope.ServiceProvider.GetRequiredService<IMaterialShortageService>();
        var row = (await service.ListAsync()).Single(x => x.RawMaterialCode == "RM-001");

        Assert.Equal(5_000, row.TotalRequirement);
        Assert.Equal(3_750, row.OnHandAvailable);
        Assert.Equal(0, row.EligibleIncoming);
        Assert.Equal(3_750, row.ProjectedAvailable);
        Assert.Equal(1_250, row.ShortageQuantity);
        Assert.Equal(plan.PlannedCompletionDate, row.MaterialRequiredDate);
        Assert.Equal(plan.PlannedCompletionDate, row.EvaluationDate);
        Assert.Contains(row.AffectedOrders, x => x.PlanNumber == "PP-DEMO-001" && x.OrderNumber == "SO-DEMO-001");
    }

    [Fact]
    public async Task Late_purchase_order_is_reported_for_visibility_but_never_counted()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materialId = await db.RawMaterials.Where(x => x.Code == "RM-002").Select(x => x.Id).SingleAsync();
        await AddIncomingPurchaseOrderAsync(db, "PO-LATE-RM002", materialId, 1_000, TestTime.AddDays(-4), IncomingPurchaseOrderStatus.Open);

        var service = scope.ServiceProvider.GetRequiredService<IMaterialShortageService>();
        var row = (await service.ListAsync()).Single(x => x.RawMaterialId == materialId);
        var late = Assert.Single(row.LatePurchaseOrders);

        Assert.Equal("PO-LATE-RM002", late.PurchaseOrderNumber);
        Assert.Equal(4, late.DelayDays);
        Assert.Equal(1_000, late.OutstandingQuantity);
        Assert.Equal(0, row.EligibleIncoming);
    }

    [Fact]
    public async Task Material_without_active_demand_has_no_shortage_row_and_refuses_a_purchase_request()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var material = await AddRawMaterialAsync(db, "RM-IDLE", currentStock: 10);
        var anyPlanId = await db.ProductionPlans.Select(x => x.Id).FirstAsync();

        var service = scope.ServiceProvider.GetRequiredService<IMaterialShortageService>();
        Assert.Null(await service.GetAsync(material));
        Assert.DoesNotContain(await service.ListAsync(), x => x.RawMaterialId == material);

        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");
        using var response = await SendJsonAsync(client, "/api/material-shortages/purchase-requests",
            new CreatePurchaseRequestCommand(material, anyPlanId, 5, TestTime.AddDays(3)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Purchase_request_is_created_once_and_a_duplicate_returns_conflict()
    {
        var fixture = await CreateShortageFixtureAsync("DUP");
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");
        var command = new CreatePurchaseRequestCommand(fixture.RawMaterialId, fixture.ProductionPlanId, 400, TestTime.AddDays(3));

        using var first = await SendJsonAsync(client, "/api/material-shortages/purchase-requests", command);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var created = (await first.Content.ReadFromJsonAsync<PurchaseRequestDto>())!;
        Assert.Equal(PurchaseRequestStatus.Draft, created.Status);
        Assert.Equal(400, Assert.Single(created.Items).RequestedQuantity);

        using var duplicate = await SendJsonAsync(client, "/api/material-shortages/purchase-requests", command);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var active = await db.PurchaseRequests
            .CountAsync(x => x.SourceProductionPlanId == fixture.ProductionPlanId
                && PurchaseRequestRules.ActiveStatuses.Contains(x.Status)
                && x.Items.Any(item => item.RawMaterialId == fixture.RawMaterialId));
        Assert.Equal(1, active);
    }

    [Fact]
    public async Task Approved_request_does_not_block_a_new_one()
    {
        var fixture = await CreateShortageFixtureAsync("APPROVED");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await AddPurchaseRequestAsync(db, fixture, PurchaseRequestStatus.Approved);
        }

        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");
        using var response = await SendJsonAsync(client, "/api/material-shortages/purchase-requests",
            new CreatePurchaseRequestCommand(fixture.RawMaterialId, fixture.ProductionPlanId, 100, TestTime.AddDays(3)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Requested_quantity_above_the_current_shortage_is_rejected()
    {
        var fixture = await CreateShortageFixtureAsync("OVER");
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");

        using var response = await SendJsonAsync(client, "/api/material-shortages/purchase-requests",
            new CreatePurchaseRequestCommand(fixture.RawMaterialId, fixture.ProductionPlanId, 401, TestTime.AddDays(3)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_can_read_shortages_but_cannot_raise_a_purchase_request()
    {
        var fixture = await CreateShortageFixtureAsync("VIEWER");
        using var client = CreateClient();
        await LoginAsync(client, "viewer.demo");

        using var read = await client.GetAsync("/api/material-shortages");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.NotEmpty((await read.Content.ReadFromJsonAsync<MaterialShortageDto[]>())!);

        using var write = await SendJsonAsync(client, "/api/material-shortages/purchase-requests",
            new CreatePurchaseRequestCommand(fixture.RawMaterialId, fixture.ProductionPlanId, 10, TestTime.AddDays(3)));
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task Anonymous_shortage_read_is_rejected()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/api/material-shortages");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record ShortageFixture(long RawMaterialId, long ProductionPlanId);

    /// <summary>Stock 100 against a single active plan requiring 500, so the shortage is always 400.</summary>
    private async Task<ShortageFixture> CreateShortageFixtureAsync(string suffix)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materialId = await AddRawMaterialAsync(db, $"RM-{suffix}", currentStock: 100);
        var machineId = await db.Machines.Select(x => x.Id).FirstAsync();
        var userId = await db.CustomerOrders.Select(x => x.CreatedByUserId).FirstAsync();

        var formulation = new Formulation
        {
            Code = $"FM-{suffix}",
            Name = $"Shortage Formulation {suffix}",
            BatchSize = 10,
            CreatedAt = TestTime,
            UpdatedAt = TestTime,
            Materials = [new FormulationMaterial { RawMaterialId = materialId, WeightPerBatch = 10 }]
        };
        db.Formulations.Add(formulation);
        await db.SaveChangesAsync();

        var order = new CustomerOrder
        {
            OrderNumber = $"SO-{suffix}",
            CustomerName = $"Shortage Customer {suffix}",
            FormulationId = formulation.Id,
            Quantity = 500,
            DeliveryDate = TestTime.AddDays(4),
            Priority = "High",
            Status = CustomerOrderStatus.Planned,
            CreatedByUserId = userId,
            CreatedAt = TestTime,
            UpdatedAt = TestTime
        };
        db.CustomerOrders.Add(order);
        await db.SaveChangesAsync();

        var plan = new ProductionPlan
        {
            PlanNumber = $"PP-{suffix}",
            CustomerOrderId = order.Id,
            MachineId = machineId,
            RequiredBatch = 50,
            PlannedCompletionDate = TestTime.AddDays(3),
            Status = ProductionPlanStatus.Planned,
            CreatedByUserId = userId,
            CreatedAt = TestTime,
            MaterialRequirements =
            [
                new MaterialRequirement { RawMaterialId = materialId, RequiredQuantity = 500, CalculatedAt = TestTime }
            ]
        };
        db.ProductionPlans.Add(plan);
        await db.SaveChangesAsync();

        return new ShortageFixture(materialId, plan.Id);
    }

    private static async Task<long> AddRawMaterialAsync(AppDbContext db, string code, decimal currentStock)
    {
        var material = new RawMaterial
        {
            Code = code,
            Name = $"Material {code}",
            Unit = "kg",
            CurrentStock = currentStock,
            ReservedStock = 0,
            LeadTimeDays = 3,
            CreatedAt = TestTime,
            UpdatedAt = TestTime
        };
        db.RawMaterials.Add(material);
        await db.SaveChangesAsync();
        return material.Id;
    }

    private static async Task AddPurchaseRequestAsync(AppDbContext db, ShortageFixture fixture, PurchaseRequestStatus status)
    {
        var userId = await db.CustomerOrders.Select(x => x.CreatedByUserId).FirstAsync();
        db.PurchaseRequests.Add(new PurchaseRequest
        {
            RequestNumber = $"PR-SEED-{fixture.ProductionPlanId}",
            SourceProductionPlanId = fixture.ProductionPlanId,
            Status = status,
            RequestedByUserId = userId,
            RequestedDate = TestTime,
            ApprovedByUserId = status == PurchaseRequestStatus.Approved ? userId : null,
            ApprovedDate = status == PurchaseRequestStatus.Approved ? TestTime : null,
            CreatedAt = TestTime,
            Items =
            [
                new PurchaseRequestItem { RawMaterialId = fixture.RawMaterialId, RequestedQuantity = 50, ExpectedDate = TestTime.AddDays(2) }
            ]
        });
        await db.SaveChangesAsync();
    }

    private static async Task AddIncomingPurchaseOrderAsync(
        AppDbContext db, string purchaseOrderNumber, long rawMaterialId, decimal orderedQuantity,
        DateTime expectedDate, IncomingPurchaseOrderStatus status)
    {
        var sourcePlanId = await db.ProductionPlans.Select(x => x.Id).FirstAsync();
        var userId = await db.CustomerOrders.Select(x => x.CreatedByUserId).FirstAsync();
        var request = new PurchaseRequest
        {
            RequestNumber = $"PR-{purchaseOrderNumber}",
            SourceProductionPlanId = sourcePlanId,
            Status = PurchaseRequestStatus.Approved,
            RequestedByUserId = userId,
            RequestedDate = TestTime,
            CreatedAt = TestTime
        };
        db.PurchaseRequests.Add(request);
        await db.SaveChangesAsync();

        db.IncomingPurchaseOrders.Add(new IncomingPurchaseOrder
        {
            PurchaseOrderNumber = purchaseOrderNumber,
            PurchaseRequestId = request.Id,
            ExpectedDate = expectedDate,
            Status = status,
            CreatedAt = TestTime,
            Items = [new IncomingPurchaseOrderItem { RawMaterialId = rawMaterialId, OrderedQuantity = orderedQuantity, ReceivedQuantity = 0 }]
        });
        await db.SaveChangesAsync();
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

    private static async Task<HttpResponseMessage> SendJsonAsync<T>(HttpClient client, string uri, T payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-XSRF-TOKEN", await GetTokenAsync(client));
        return await client.SendAsync(request);
    }

    private static async Task<string> GetTokenAsync(HttpClient client) => (await client.GetFromJsonAsync<TokenResponse>("/api/auth/antiforgery"))!.Token;
    private sealed record TokenResponse(string Token);
}
