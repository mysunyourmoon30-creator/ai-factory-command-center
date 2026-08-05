using System.Net;
using System.Net.Http.Json;
using AI.Factory.Core.Domain;
using AI.Factory.Core.Production;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Factory.IntegrationTests;

public sealed class MaterialRequirementTests : IClassFixture<AiFactoryWebApplicationFactory>
{
    private static readonly DateTime TestTime = new(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc);
    private readonly AiFactoryWebApplicationFactory _factory;
    public MaterialRequirementTests(AiFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Late_purchase_order_is_excluded_so_demo_material_projects_three_thousand_seven_hundred_fifty()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var material = await db.RawMaterials.AsNoTracking().SingleAsync(x => x.Code == "RM-001");
        var plan = await db.ProductionPlans.AsNoTracking().SingleAsync(x => x.PlanNumber == "PP-DEMO-001");
        await AddIncomingPurchaseOrderAsync(db, "PO-LATE-RM001", material.Id, orderedQuantity: 1_000, expectedDate: TestTime.AddDays(-4));

        var service = scope.ServiceProvider.GetRequiredService<IMaterialRequirementQueryService>();
        var row = (await service.ListAvailabilityAsync()).Single(x => x.RawMaterialId == material.Id);

        Assert.Equal(4_200, row.CurrentStock);
        Assert.Equal(450, row.ReservedStock);
        Assert.Equal(3_750, row.OnHandAvailable);
        Assert.Equal(plan.PlannedCompletionDate, row.EvaluationDate);
        Assert.Equal(5_000, row.RequiredQuantity);
        Assert.Equal(0, row.EligibleIncoming);
        Assert.Equal(3_750, row.ProjectedAvailable);
    }

    [Fact]
    public async Task Multiple_plans_evaluate_each_date_so_later_supply_is_not_cut_off()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var firstDate = TestTime.AddDays(2);
        var secondDate = TestTime.AddDays(6);
        var materialId = await AddCumulativeCaseAsync(db, "RM-CASE-B", firstDate, secondDate);

        var service = scope.ServiceProvider.GetRequiredService<IMaterialRequirementQueryService>();
        var row = (await service.ListAvailabilityAsync()).Single(x => x.RawMaterialId == materialId);
        var first = row.Timeline.Single(x => x.RequiredDate == firstDate);
        var second = row.Timeline.Single(x => x.RequiredDate == secondDate);

        Assert.Equal(80, first.CumulativeRequired);
        Assert.Equal(0, first.CumulativeIncoming);
        Assert.Equal(100, first.AvailableByDate);
        Assert.Equal(150, second.CumulativeRequired);
        Assert.Equal(50, second.CumulativeIncoming);
        Assert.Equal(150, second.AvailableByDate);
        Assert.Equal(150, row.RequiredQuantity);
        Assert.Equal(150, row.ProjectedAvailable);
    }

    [Fact]
    public async Task Completed_plans_are_not_active_demand()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var completed = await db.ProductionPlans.AsNoTracking()
            .Include(x => x.MaterialRequirements)
            .SingleAsync(x => x.PlanNumber == "PP-0004");
        Assert.Equal(ProductionPlanStatus.Completed, completed.Status);

        var service = scope.ServiceProvider.GetRequiredService<IMaterialRequirementQueryService>();
        var availability = await service.ListAvailabilityAsync();

        foreach (var requirement in completed.MaterialRequirements)
        {
            var row = availability.Single(x => x.RawMaterialId == requirement.RawMaterialId);
            Assert.DoesNotContain(row.Timeline, point => point.RequiredDate == completed.PlannedCompletionDate);
        }
    }

    [Fact]
    public async Task Plan_requirement_view_reports_the_plan_quantity_and_its_cumulative_context()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var plan = await db.ProductionPlans.AsNoTracking().SingleAsync(x => x.PlanNumber == "PP-DEMO-001");

        var service = scope.ServiceProvider.GetRequiredService<IMaterialRequirementQueryService>();
        var result = await service.GetByProductionPlanAsync(plan.Id);

        Assert.NotNull(result);
        Assert.Equal("PP-DEMO-001", result.PlanNumber);
        Assert.Equal(20, result.RequiredBatch);
        var line = result.Lines.Single(x => x.RawMaterialCode == "RM-001");
        Assert.Equal(5_000, line.PlanRequiredQuantity);
        Assert.Equal(5_000, line.CumulativeRequired);
        Assert.Equal(3_750, line.OnHandAvailable);
        Assert.Equal(3_750, line.ProjectedAvailable);
    }

    [Fact]
    public async Task Unknown_production_plan_returns_not_found()
    {
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");

        using var response = await client.GetAsync("/api/material-requirements/production-plans/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("admin.demo")]
    [InlineData("manager.demo")]
    [InlineData("planner.demo")]
    [InlineData("viewer.demo")]
    public async Task Every_authenticated_role_can_read_material_requirements(string username)
    {
        using var client = CreateClient();
        await LoginAsync(client, username);

        using var response = await client.GetAsync("/api/material-requirements");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty((await response.Content.ReadFromJsonAsync<MaterialAvailabilityDto[]>())!);
    }

    [Fact]
    public async Task Anonymous_material_requirement_read_is_rejected()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/api/material-requirements");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<long> AddCumulativeCaseAsync(AppDbContext db, string materialCode, DateTime firstDate, DateTime secondDate)
    {
        var machineId = await db.Machines.Where(x => x.MachineCode == "Machine-01").Select(x => x.Id).FirstAsync();
        var userId = await db.CustomerOrders.Select(x => x.CreatedByUserId).FirstAsync();
        var material = new RawMaterial
        {
            Code = materialCode,
            Name = "Cumulative Case Material",
            Unit = "kg",
            CurrentStock = 100,
            ReservedStock = 0,
            LeadTimeDays = 5,
            CreatedAt = TestTime,
            UpdatedAt = TestTime
        };
        db.RawMaterials.Add(material);
        await db.SaveChangesAsync();

        var formulation = new Formulation
        {
            Code = $"FM-{materialCode}",
            Name = "Cumulative Case Formulation",
            BatchSize = 10,
            CreatedAt = TestTime,
            UpdatedAt = TestTime,
            Materials = [new FormulationMaterial { RawMaterialId = material.Id, WeightPerBatch = 10 }]
        };
        db.Formulations.Add(formulation);
        await db.SaveChangesAsync();

        await AddPlanAsync(db, formulation.Id, machineId, userId, material.Id, $"{materialCode}-A", firstDate, 80);
        await AddPlanAsync(db, formulation.Id, machineId, userId, material.Id, $"{materialCode}-B", secondDate, 70);
        await AddIncomingPurchaseOrderAsync(db, $"PO-{materialCode}", material.Id, orderedQuantity: 50, expectedDate: secondDate);
        return material.Id;
    }

    private static async Task AddPlanAsync(
        AppDbContext db, long formulationId, long machineId, long userId, long rawMaterialId,
        string suffix, DateTime requiredDate, decimal requiredQuantity)
    {
        var order = new CustomerOrder
        {
            OrderNumber = $"SO-{suffix}",
            CustomerName = "Cumulative Case Customer",
            FormulationId = formulationId,
            Quantity = requiredQuantity,
            DeliveryDate = requiredDate,
            Priority = "Normal",
            Status = CustomerOrderStatus.Planned,
            CreatedByUserId = userId,
            CreatedAt = TestTime,
            UpdatedAt = TestTime
        };
        db.CustomerOrders.Add(order);
        await db.SaveChangesAsync();

        db.ProductionPlans.Add(new ProductionPlan
        {
            PlanNumber = $"PP-{suffix}",
            CustomerOrderId = order.Id,
            MachineId = machineId,
            RequiredBatch = 1,
            PlannedCompletionDate = requiredDate,
            Status = ProductionPlanStatus.Planned,
            CreatedByUserId = userId,
            CreatedAt = TestTime,
            MaterialRequirements =
            [
                new MaterialRequirement { RawMaterialId = rawMaterialId, RequiredQuantity = requiredQuantity, CalculatedAt = TestTime }
            ]
        });
        await db.SaveChangesAsync();
    }

    private static async Task AddIncomingPurchaseOrderAsync(
        AppDbContext db, string purchaseOrderNumber, long rawMaterialId, decimal orderedQuantity, DateTime expectedDate)
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
            Status = IncomingPurchaseOrderStatus.Open,
            CreatedAt = TestTime,
            Items =
            [
                new IncomingPurchaseOrderItem { RawMaterialId = rawMaterialId, OrderedQuantity = orderedQuantity, ReceivedQuantity = 0 }
            ]
        });
        await db.SaveChangesAsync();
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
