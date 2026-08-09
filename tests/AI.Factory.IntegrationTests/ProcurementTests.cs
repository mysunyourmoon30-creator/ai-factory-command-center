using System.Net;
using System.Net.Http.Json;
using AI.Factory.Core.Domain;
using AI.Factory.Core.Production;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Factory.IntegrationTests;

/// <summary>Purchase Request lifecycle: Submit, Approve, Reject.</summary>
public sealed class ProcurementTests : IClassFixture<AiFactoryWebApplicationFactory>
{
    private static readonly DateTime TestTime = new(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc);
    private readonly AiFactoryWebApplicationFactory _factory;
    public ProcurementTests(AiFactoryWebApplicationFactory factory) => _factory = factory;

    /// <summary>
    /// Locked "Seed Data ต้องมี" requirement: 1 existing Purchase Request, 1 existing Incoming PO.
    /// PR-BASE-001 is seeded Approved (not Draft/PendingApproval) so it never blocks a new PR for
    /// the same plan+material, and PO-BASE-001's ExpectedDate is set after PP-DEMO-001's Required
    /// Date so it's excluded from Cumulative Incoming at that date - this test is the concrete
    /// proof that RM-001's locked 1,250 kg shortage and 0 EligibleIncoming are unaffected.
    /// </summary>
    [Fact]
    public async Task Canonical_seed_includes_one_approved_purchase_request_and_one_open_incoming_po()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var request = await db.PurchaseRequests.Include(x => x.Items)
            .SingleAsync(x => x.RequestNumber == "PR-BASE-001");
        Assert.Equal(PurchaseRequestStatus.Approved, request.Status);
        Assert.Equal(500, Assert.Single(request.Items).RequestedQuantity);

        var order = await db.IncomingPurchaseOrders.Include(x => x.Items)
            .SingleAsync(x => x.PurchaseOrderNumber == "PO-BASE-001");
        Assert.Equal(IncomingPurchaseOrderStatus.Open, order.Status);
        Assert.Equal(request.Id, order.PurchaseRequestId);
        Assert.Equal(500, Assert.Single(order.Items).OrderedQuantity);
        Assert.Equal(0, Assert.Single(order.Items).ReceivedQuantity);

        var shortageService = scope.ServiceProvider.GetRequiredService<IMaterialShortageService>();
        var rm001 = (await shortageService.ListAsync()).Single(x => x.RawMaterialCode == "RM-001");
        Assert.Equal(1_250, rm001.ShortageQuantity);
        Assert.Equal(0, rm001.EligibleIncoming);
    }

    /// <summary>
    /// The seed used to hold one Approved request and one Open order, leaving 5 of the 7 procurement
    /// statuses with nothing behind them - so the screens that exist to show a lifecycle showed one
    /// point on it. Every status now has a real row.
    ///
    /// The second half is the part that matters: the added rows must be invisible to the locked demo
    /// case. They are, by construction - purchase requests are never read by the availability engine,
    /// only Open and Partial orders count as incoming supply and the one Partial row sits on RM-004
    /// which has no shortage, and no added order is late at T. Asserted here rather than trusted,
    /// because a future edit to this seed would otherwise move a locked figure silently.
    /// </summary>
    [Fact]
    public async Task Canonical_seed_covers_every_procurement_status_without_moving_the_locked_figures()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Superset, and anchored to the seeded row numbers below rather than to status uniqueness:
        // sibling tests in this class share one fixture and legitimately mutate orders - Test 12
        // records receipts against PO-BASE-001, which turns it Partial mid-run.
        var requestStatuses = await db.PurchaseRequests.Select(x => x.Status).Distinct().ToListAsync();
        Assert.All(Enum.GetValues<PurchaseRequestStatus>(), status => Assert.Contains(status, requestStatuses));

        var orderStatuses = await db.IncomingPurchaseOrders.Select(x => x.Status).Distinct().ToListAsync();
        Assert.All(Enum.GetValues<IncomingPurchaseOrderStatus>(), status => Assert.Contains(status, orderStatuses));

        var partial = await db.IncomingPurchaseOrders.Include(x => x.Items)
            .SingleAsync(x => x.PurchaseOrderNumber == "PO-BASE-002");
        Assert.Equal(IncomingPurchaseOrderStatus.Partial, partial.Status);
        var partialItem = Assert.Single(partial.Items);
        Assert.True(partialItem.ReceivedQuantity > 0 && partialItem.ReceivedQuantity < partialItem.OrderedQuantity,
            "A Partial order must be genuinely part-received, not a relabelled Open or Received one.");

        var received = await db.IncomingPurchaseOrders.Include(x => x.Items)
            .SingleAsync(x => x.PurchaseOrderNumber == "PO-BASE-003");
        Assert.Equal(IncomingPurchaseOrderStatus.Received, received.Status);
        Assert.Equal(Assert.Single(received.Items).OrderedQuantity, Assert.Single(received.Items).ReceivedQuantity);
        Assert.NotNull(received.ReceivedDate);

        var rejected = await db.PurchaseRequests.SingleAsync(x => x.RequestNumber == "PR-BASE-005");
        Assert.Equal(PurchaseRequestStatus.Rejected, rejected.Status);
        Assert.False(string.IsNullOrWhiteSpace(rejected.RejectionReason));
        Assert.Null(rejected.ApprovedByUserId);

        // No added request may block the demo's own "create a purchase request for RM-001" flow.
        var demoPlan = await db.ProductionPlans.SingleAsync(x => x.PlanNumber == "PP-DEMO-001");
        var blocking = await db.PurchaseRequests
            .Where(x => x.SourceProductionPlanId == demoPlan.Id && x.RequestNumber != "PR-BASE-001"
                && (x.Status == PurchaseRequestStatus.Draft || x.Status == PurchaseRequestStatus.PendingApproval))
            .CountAsync();
        Assert.Equal(0, blocking);

        // The locked KPI figures are asserted by DashboardTests, which holds its own fixture and so
        // sees an unmutated seed. Repeating them here would be flaky for the reason noted above.
    }

    [Fact]
    public async Task Submit_moves_draft_to_pending_approval()
    {
        var fixture = await CreatePurchaseRequestAsync("SUB", PurchaseRequestStatus.Draft);
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");

        using var response = await SendJsonAsync(client, $"/api/purchase-requests/{fixture.Id}/submit", new SubmitPurchaseRequestCommand(fixture.RowVersion));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<PurchaseRequestDto>())!;
        Assert.Equal(PurchaseRequestStatus.PendingApproval, dto.Status);
    }

    /// <summary>A row version that can never match the stored value, mirroring the established
    /// idiom in ProductionPlanTests/CustomerOrderTests rather than relying on the InMemory
    /// provider to regenerate a row version on save.</summary>
    [Fact]
    public async Task Submit_with_a_wrong_row_version_returns_conflict()
    {
        var fixture = await CreatePurchaseRequestAsync("SUB-STALE", PurchaseRequestStatus.Draft);
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");

        using var response = await SendJsonAsync(client, $"/api/purchase-requests/{fixture.Id}/submit", new SubmitPurchaseRequestCommand([1]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Submit_a_request_that_is_not_draft_is_rejected()
    {
        var fixture = await CreatePurchaseRequestAsync("SUB-BADSTATUS", PurchaseRequestStatus.PendingApproval);
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");

        using var response = await SendJsonAsync(client, $"/api/purchase-requests/{fixture.Id}/submit", new SubmitPurchaseRequestCommand(fixture.RowVersion));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Approve_sets_the_approver_and_the_approved_date()
    {
        var fixture = await CreatePurchaseRequestAsync("APR", PurchaseRequestStatus.PendingApproval);
        using var client = CreateClient();
        await LoginAsync(client, "manager.demo");

        using var response = await SendJsonAsync(client, $"/api/purchase-requests/{fixture.Id}/approve", new ApprovePurchaseRequestCommand(fixture.RowVersion));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<PurchaseRequestDto>())!;
        Assert.Equal(PurchaseRequestStatus.Approved, dto.Status);
        Assert.NotNull(dto.ApprovedDate);
    }

    [Fact]
    public async Task Reject_requires_a_reason()
    {
        var fixture = await CreatePurchaseRequestAsync("REJ", PurchaseRequestStatus.PendingApproval);
        using var client = CreateClient();
        await LoginAsync(client, "manager.demo");

        using var empty = await SendJsonAsync(client, $"/api/purchase-requests/{fixture.Id}/reject", new RejectPurchaseRequestCommand("", fixture.RowVersion));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        using var response = await SendJsonAsync(client, $"/api/purchase-requests/{fixture.Id}/reject", new RejectPurchaseRequestCommand("Price too high", fixture.RowVersion));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<PurchaseRequestDto>())!;
        Assert.Equal(PurchaseRequestStatus.Rejected, dto.Status);
        Assert.Equal("Price too high", dto.RejectionReason);
    }

    [Fact]
    public async Task Approved_and_rejected_requests_never_transition_again()
    {
        var approved = await CreatePurchaseRequestAsync("LOCKED-APR", PurchaseRequestStatus.Approved);
        var rejected = await CreatePurchaseRequestAsync("LOCKED-REJ", PurchaseRequestStatus.Rejected);
        using var client = CreateClient();
        await LoginAsync(client, "manager.demo");

        using var reApprove = await SendJsonAsync(client, $"/api/purchase-requests/{approved.Id}/approve", new ApprovePurchaseRequestCommand(approved.RowVersion));
        Assert.Equal(HttpStatusCode.BadRequest, reApprove.StatusCode);

        using var reReject = await SendJsonAsync(client, $"/api/purchase-requests/{rejected.Id}/reject", new RejectPurchaseRequestCommand("again", rejected.RowVersion));
        Assert.Equal(HttpStatusCode.BadRequest, reReject.StatusCode);
    }

    [Fact]
    public async Task Planner_can_submit_but_not_approve_reject_or_record_an_incoming_order()
    {
        var pending = await CreatePurchaseRequestAsync("ROLE-PLANNER-PENDING", PurchaseRequestStatus.PendingApproval);
        var approved = await CreatePurchaseRequestAsync("ROLE-PLANNER-APPROVED", PurchaseRequestStatus.Approved, requestedQuantity: 300);
        using var client = CreateClient();
        await LoginAsync(client, "planner.demo");

        using var read = await client.GetAsync("/api/purchase-requests");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        using var approve = await SendJsonAsync(client, $"/api/purchase-requests/{pending.Id}/approve", new ApprovePurchaseRequestCommand(pending.RowVersion));
        Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);

        using var reject = await SendJsonAsync(client, $"/api/purchase-requests/{pending.Id}/reject", new RejectPurchaseRequestCommand("no", pending.RowVersion));
        Assert.Equal(HttpStatusCode.Forbidden, reject.StatusCode);

        using var createOrder = await SendJsonAsync(client, "/api/incoming-purchase-orders",
            new CreateIncomingPurchaseOrderCommand(approved.Id, TestTime.AddDays(5), [new IncomingPurchaseOrderItemCommand(approved.RawMaterialId, 300)]));
        Assert.Equal(HttpStatusCode.Forbidden, createOrder.StatusCode);
    }

    [Fact]
    public async Task Anonymous_procurement_read_is_rejected()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/api/purchase-requests");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
            RejectionReason = status == PurchaseRequestStatus.Rejected ? "Seeded as rejected" : null,
            Items = [new PurchaseRequestItem { RawMaterialId = material.Id, RequestedQuantity = requestedQuantity, ExpectedDate = TestTime.AddDays(4) }]
        };
        db.PurchaseRequests.Add(request);
        await db.SaveChangesAsync();

        return new PurchaseRequestFixture(request.Id, request.RowVersion, material.Id, plan.Id);
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
