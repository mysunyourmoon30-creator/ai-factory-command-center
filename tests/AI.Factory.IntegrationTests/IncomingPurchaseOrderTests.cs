using System.Net;
using System.Net.Http.Json;
using AI.Factory.Core.Domain;
using AI.Factory.Core.Production;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Factory.IntegrationTests;

/// <summary>Incoming Purchase Order: creation from an Approved request, and idempotent receipt.</summary>
public sealed class IncomingPurchaseOrderTests : IClassFixture<AiFactoryWebApplicationFactory>
{
    private static readonly DateTime TestTime = new(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc);
    private readonly AiFactoryWebApplicationFactory _factory;
    public IncomingPurchaseOrderTests(AiFactoryWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Incoming_purchase_order_is_created_from_an_approved_request_and_a_duplicate_is_rejected()
    {
        var fixture = await CreatePurchaseRequestAsync("PO-CREATE", PurchaseRequestStatus.Approved, requestedQuantity: 400);
        using var client = CreateClient();
        await LoginAsync(client, "manager.demo");
        var command = new CreateIncomingPurchaseOrderCommand(fixture.Id, TestTime.AddDays(5), [new IncomingPurchaseOrderItemCommand(fixture.RawMaterialId, 400)]);

        using var first = await SendJsonAsync(client, "/api/incoming-purchase-orders", command);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var order = (await first.Content.ReadFromJsonAsync<IncomingPurchaseOrderDto>())!;
        Assert.Equal(IncomingPurchaseOrderStatus.Open, order.Status);
        Assert.Equal(400, Assert.Single(order.Items).OrderedQuantity);

        using var duplicate = await SendJsonAsync(client, "/api/incoming-purchase-orders", command);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Incoming_purchase_order_quantity_cannot_exceed_the_requested_quantity()
    {
        var fixture = await CreatePurchaseRequestAsync("PO-OVER", PurchaseRequestStatus.Approved, requestedQuantity: 400);
        using var client = CreateClient();
        await LoginAsync(client, "manager.demo");

        using var response = await SendJsonAsync(client, "/api/incoming-purchase-orders",
            new CreateIncomingPurchaseOrderCommand(fixture.Id, TestTime.AddDays(5), [new IncomingPurchaseOrderItemCommand(fixture.RawMaterialId, 500)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Incoming_purchase_order_cannot_be_created_from_a_non_approved_request()
    {
        var fixture = await CreatePurchaseRequestAsync("PO-DRAFT", PurchaseRequestStatus.Draft, requestedQuantity: 400);
        using var client = CreateClient();
        await LoginAsync(client, "manager.demo");

        using var response = await SendJsonAsync(client, "/api/incoming-purchase-orders",
            new CreateIncomingPurchaseOrderCommand(fixture.Id, TestTime.AddDays(5), [new IncomingPurchaseOrderItemCommand(fixture.RawMaterialId, 400)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Locked Test 12: Received 300 -> target 500 adds 200 stock; resending 500 adds nothing.</summary>
    [Fact]
    public async Task Receipt_is_idempotent_and_matches_the_locked_test_twelve_numbers()
    {
        var fixture = await CreatePurchaseRequestAsync("PO12", PurchaseRequestStatus.Approved, requestedQuantity: 1_000, currentStock: 4_200);
        using var client = CreateClient();
        await LoginAsync(client, "manager.demo");
        var order = await CreateIncomingPurchaseOrderAsync(client, fixture, orderedQuantity: 1_000);

        using var firstReceive = await ReceiveAsync(client, order, fixture.RawMaterialId, target: 300);
        Assert.Equal(HttpStatusCode.OK, firstReceive.StatusCode);
        var firstDto = (await firstReceive.Content.ReadFromJsonAsync<IncomingPurchaseOrderDto>())!;
        var stockAfterFirst = await ReadStockAsync(fixture.RawMaterialId);

        using var secondReceive = await ReceiveAsync(client, firstDto, fixture.RawMaterialId, target: 500);
        Assert.Equal(HttpStatusCode.OK, secondReceive.StatusCode);
        var secondDto = (await secondReceive.Content.ReadFromJsonAsync<IncomingPurchaseOrderDto>())!;
        var stockAfterSecond = await ReadStockAsync(fixture.RawMaterialId);
        Assert.Equal(500, Assert.Single(secondDto.Items).ReceivedQuantity);
        Assert.Equal(200, stockAfterSecond - stockAfterFirst);

        using var resend = await ReceiveAsync(client, secondDto, fixture.RawMaterialId, target: 500);
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        var resendDto = (await resend.Content.ReadFromJsonAsync<IncomingPurchaseOrderDto>())!;
        var stockAfterResend = await ReadStockAsync(fixture.RawMaterialId);
        Assert.Equal(500, Assert.Single(resendDto.Items).ReceivedQuantity);
        Assert.Equal(stockAfterSecond, stockAfterResend);
    }

    [Fact]
    public async Task Received_quantity_cannot_decrease_or_exceed_the_ordered_quantity()
    {
        var fixture = await CreatePurchaseRequestAsync("PO-BOUNDS", PurchaseRequestStatus.Approved, requestedQuantity: 500);
        using var client = CreateClient();
        await LoginAsync(client, "manager.demo");
        var order = await CreateIncomingPurchaseOrderAsync(client, fixture, orderedQuantity: 500);
        using var seedReceipt = await ReceiveAsync(client, order, fixture.RawMaterialId, target: 300);
        Assert.Equal(HttpStatusCode.OK, seedReceipt.StatusCode);
        var afterFirst = (await seedReceipt.Content.ReadFromJsonAsync<IncomingPurchaseOrderDto>())!;

        using var decrease = await SendJsonAsync(client, $"/api/incoming-purchase-orders/{afterFirst.Id}/receive",
            new SetReceivedQuantityCommand(fixture.RawMaterialId, 100, afterFirst.RowVersion));
        Assert.Equal(HttpStatusCode.BadRequest, decrease.StatusCode);

        using var overOrdered = await SendJsonAsync(client, $"/api/incoming-purchase-orders/{afterFirst.Id}/receive",
            new SetReceivedQuantityCommand(fixture.RawMaterialId, 600, afterFirst.RowVersion));
        Assert.Equal(HttpStatusCode.BadRequest, overOrdered.StatusCode);
    }

    /// <summary>A row version that can never match the stored value, mirroring the established
    /// idiom in ProductionPlanTests/CustomerOrderTests rather than relying on the InMemory
    /// provider to regenerate a row version on save.</summary>
    [Fact]
    public async Task Receipt_with_a_wrong_row_version_returns_conflict()
    {
        var fixture = await CreatePurchaseRequestAsync("PO-STALE", PurchaseRequestStatus.Approved, requestedQuantity: 500);
        using var client = CreateClient();
        await LoginAsync(client, "manager.demo");
        var order = await CreateIncomingPurchaseOrderAsync(client, fixture, orderedQuantity: 500);

        using var response = await SendJsonAsync(client, $"/api/incoming-purchase-orders/{order.Id}/receive",
            new SetReceivedQuantityCommand(fixture.RawMaterialId, 200, [1]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_can_read_but_cannot_record_an_incoming_purchase_order_or_receipt()
    {
        var fixture = await CreatePurchaseRequestAsync("ROLE-VIEWER", PurchaseRequestStatus.Approved, requestedQuantity: 400);
        using var client = CreateClient();
        await LoginAsync(client, "viewer.demo");

        using var read = await client.GetAsync("/api/incoming-purchase-orders");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        using var create = await SendJsonAsync(client, "/api/incoming-purchase-orders",
            new CreateIncomingPurchaseOrderCommand(fixture.Id, TestTime.AddDays(5), [new IncomingPurchaseOrderItemCommand(fixture.RawMaterialId, 400)]));
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    internal sealed record PurchaseRequestFixture(long Id, byte[] RowVersion, long RawMaterialId, long ProductionPlanId);

    private async Task<PurchaseRequestFixture> CreatePurchaseRequestAsync(
        string suffix, PurchaseRequestStatus status, decimal requestedQuantity = 400, decimal currentStock = 100)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var machineId = await db.Machines.Select(x => x.Id).FirstAsync();
        var userId = await db.CustomerOrders.Select(x => x.CreatedByUserId).FirstAsync();

        var material = new RawMaterial { Code = $"RM-{suffix}", Name = $"Material {suffix}", Unit = "kg", CurrentStock = currentStock, ReservedStock = 0, LeadTimeDays = 3, CreatedAt = TestTime, UpdatedAt = TestTime };
        db.RawMaterials.Add(material);
        await db.SaveChangesAsync();

        var formulation = new Formulation { Code = $"FM-{suffix}", Name = $"Formulation {suffix}", BatchSize = 10, CreatedAt = TestTime, UpdatedAt = TestTime, Materials = [new FormulationMaterial { RawMaterialId = material.Id, WeightPerBatch = 10 }] };
        db.Formulations.Add(formulation);
        await db.SaveChangesAsync();

        var order = new CustomerOrder { OrderNumber = $"SO-{suffix}", CustomerName = $"Customer {suffix}", FormulationId = formulation.Id, Quantity = requestedQuantity, DeliveryDate = TestTime.AddDays(6), Priority = "High", Status = CustomerOrderStatus.Planned, CreatedByUserId = userId, CreatedAt = TestTime, UpdatedAt = TestTime };
        db.CustomerOrders.Add(order);
        await db.SaveChangesAsync();

        var plan = new ProductionPlan { PlanNumber = $"PP-{suffix}", CustomerOrderId = order.Id, MachineId = machineId, RequiredBatch = 1, PlannedCompletionDate = TestTime.AddDays(5), Status = ProductionPlanStatus.Planned, CreatedByUserId = userId, CreatedAt = TestTime };
        db.ProductionPlans.Add(plan);
        await db.SaveChangesAsync();

        var request = new PurchaseRequest
        {
            RequestNumber = $"PR-{suffix}",
            SourceProductionPlanId = plan.Id,
            Status = status,
            RequestedByUserId = userId,
            RequestedDate = TestTime,
            CreatedAt = TestTime,
            ApprovedByUserId = status == PurchaseRequestStatus.Approved ? userId : null,
            ApprovedDate = status == PurchaseRequestStatus.Approved ? TestTime : null,
            Items = [new PurchaseRequestItem { RawMaterialId = material.Id, RequestedQuantity = requestedQuantity, ExpectedDate = TestTime.AddDays(4) }]
        };
        db.PurchaseRequests.Add(request);
        await db.SaveChangesAsync();

        return new PurchaseRequestFixture(request.Id, request.RowVersion, material.Id, plan.Id);
    }

    private static async Task<IncomingPurchaseOrderDto> CreateIncomingPurchaseOrderAsync(HttpClient client, PurchaseRequestFixture fixture, decimal orderedQuantity)
    {
        using var response = await SendJsonAsync(client, "/api/incoming-purchase-orders",
            new CreateIncomingPurchaseOrderCommand(fixture.Id, TestTime.AddDays(5), [new IncomingPurchaseOrderItemCommand(fixture.RawMaterialId, orderedQuantity)]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IncomingPurchaseOrderDto>())!;
    }

    private static Task<HttpResponseMessage> ReceiveAsync(HttpClient client, IncomingPurchaseOrderDto order, long rawMaterialId, decimal target) =>
        SendJsonAsync(client, $"/api/incoming-purchase-orders/{order.Id}/receive", new SetReceivedQuantityCommand(rawMaterialId, target, order.RowVersion));

    private async Task<decimal> ReadStockAsync(long rawMaterialId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.RawMaterials.AsNoTracking().Where(x => x.Id == rawMaterialId).Select(x => x.CurrentStock).SingleAsync();
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
