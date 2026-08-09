using AI.Factory.Core.Domain;
using AI.Factory.Infrastructure.Identity;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Factory.Infrastructure.Production;

/// <summary>
/// Seeds the locked spec's "Existing Purchase Request 1" / "Existing Incoming PO 1" baseline, plus
/// enough extra rows that every PurchaseRequestStatus and IncomingPurchaseOrderStatus has a real
/// example to look at - the procurement screens previously showed a single Approved request and a
/// single Open order, leaving 5 of the 7 statuses with nothing behind them.
///
/// Every addition is chosen so the locked demo case survives untouched. Three rules make that hold:
///
/// 1. PR-BASE-001 stays Approved (not Draft/PendingApproval) so it never blocks a new purchase
///    request for PP-DEMO-001 + RM-001, which is the flow the demo walks through. Every added
///    request uses a different plan AND a different material, so none of them blocks it either.
/// 2. Only Open and Partial orders count as Cumulative Incoming
///    (<see cref="Core.Production.MaterialAvailabilityRules.EligibleIncomingStatuses"/>), and
///    purchase requests are never read by the availability engine at all. So the one added Partial
///    order is placed on RM-004 - which has no shortage and 9,500 kg available against 2,800 kg of
///    demand - and RM-001's locked figures (5,000 required, 3,750 available, 0 eligible incoming,
///    1,250 short at T+5) cannot move.
/// 3. Every added order is non-late at T, preserving LatePurchaseOrderCount = 0.
///
/// The Received order deliberately does not add to RawMaterials.CurrentStock. Seeded stock is the
/// opening balance, which already accounts for goods received before T; incrementing it here would
/// double-count them. Only the live receipt path (SetReceivedQuantityAsync) moves stock.
/// </summary>
public static class CanonicalProcurementSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var db = services.GetRequiredService<AppDbContext>();

        var anchor = await db.CustomerOrders.AsNoTracking().SingleOrDefaultAsync(x => x.OrderNumber == "SO-DEMO-001", cancellationToken)
            ?? throw new InvalidOperationException("Seed canonical customer orders before procurement.");
        var seedDate = DateTime.SpecifyKind(anchor.CreatedAt.Date, DateTimeKind.Utc);

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var planner = await userManager.FindByNameAsync("planner.demo")
            ?? throw new InvalidOperationException("Seed identity before procurement.");
        var manager = await userManager.FindByNameAsync("manager.demo")
            ?? throw new InvalidOperationException("Seed identity before procurement.");

        // The locked baseline pair.
        await AddAsync(db, seedDate, planner.Id, manager.Id, cancellationToken, new Row(
            "PR-BASE-001", "PP-DEMO-001", "RM-001", 500, PurchaseRequestStatus.Approved,
            PurchaseOrderNumber: "PO-BASE-001", OrderStatus: IncomingPurchaseOrderStatus.Open,
            ReceivedQuantity: 0, ExpectedDayOffset: 10));

        // Coverage for the remaining statuses. Different plan and material on every row.
        await AddAsync(db, seedDate, planner.Id, manager.Id, cancellationToken, new Row(
            "PR-BASE-002", "PP-0002", "RM-004", 400, PurchaseRequestStatus.Approved,
            PurchaseOrderNumber: "PO-BASE-002", OrderStatus: IncomingPurchaseOrderStatus.Partial,
            ReceivedQuantity: 200, ExpectedDayOffset: 8));

        await AddAsync(db, seedDate, planner.Id, manager.Id, cancellationToken, new Row(
            "PR-BASE-003", "PP-0003", "RM-008", 300, PurchaseRequestStatus.Approved,
            PurchaseOrderNumber: "PO-BASE-003", OrderStatus: IncomingPurchaseOrderStatus.Received,
            ReceivedQuantity: 300, ExpectedDayOffset: 3, ReceivedDayOffset: 3));

        await AddAsync(db, seedDate, planner.Id, manager.Id, cancellationToken, new Row(
            "PR-BASE-004", "PP-0006", "RM-006", 250, PurchaseRequestStatus.PendingApproval));

        await AddAsync(db, seedDate, planner.Id, manager.Id, cancellationToken, new Row(
            "PR-BASE-005", "PP-0008", "RM-007", 150, PurchaseRequestStatus.Rejected,
            RejectionReason: "Existing stock covers this requirement; re-raise closer to the required date."));

        await AddAsync(db, seedDate, planner.Id, manager.Id, cancellationToken, new Row(
            "PR-BASE-006", "PP-0009", "RM-009", 200, PurchaseRequestStatus.Draft));
    }

    private sealed record Row(
        string RequestNumber,
        string PlanNumber,
        string MaterialCode,
        decimal Quantity,
        PurchaseRequestStatus Status,
        string? PurchaseOrderNumber = null,
        IncomingPurchaseOrderStatus OrderStatus = IncomingPurchaseOrderStatus.Open,
        decimal ReceivedQuantity = 0,
        int ExpectedDayOffset = 10,
        int? ReceivedDayOffset = null,
        string? RejectionReason = null);

    /// <summary>
    /// Guarded per request number rather than once for the whole seeder, so re-running against a
    /// database that already holds the original baseline still picks up rows added later.
    /// </summary>
    private static async Task AddAsync(
        AppDbContext db, DateTime seedDate, long plannerId, long managerId, CancellationToken cancellationToken, Row row)
    {
        if (await db.PurchaseRequests.AnyAsync(x => x.RequestNumber == row.RequestNumber, cancellationToken))
            return;

        var plan = await db.ProductionPlans.AsNoTracking().SingleOrDefaultAsync(x => x.PlanNumber == row.PlanNumber, cancellationToken)
            ?? throw new InvalidOperationException($"Seed canonical production plans before procurement; {row.PlanNumber} is missing.");
        var rawMaterial = await db.RawMaterials.AsNoTracking().SingleAsync(x => x.Code == row.MaterialCode, cancellationToken);

        var approved = row.Status == PurchaseRequestStatus.Approved;
        var request = new PurchaseRequest
        {
            RequestNumber = row.RequestNumber,
            SourceProductionPlanId = plan.Id,
            Status = row.Status,
            RequestedByUserId = plannerId,
            RequestedDate = seedDate,
            // Rejection records a reason and no approver, matching what RejectPurchaseRequestAsync writes.
            ApprovedByUserId = approved ? managerId : null,
            ApprovedDate = approved ? seedDate : null,
            RejectionReason = row.RejectionReason,
            CreatedAt = seedDate,
            Items =
            [
                new PurchaseRequestItem
                {
                    RawMaterialId = rawMaterial.Id,
                    RequestedQuantity = row.Quantity,
                    ExpectedDate = seedDate.AddDays(row.ExpectedDayOffset)
                }
            ]
        };
        db.PurchaseRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);

        if (row.PurchaseOrderNumber is null)
            return;

        db.IncomingPurchaseOrders.Add(new IncomingPurchaseOrder
        {
            PurchaseOrderNumber = row.PurchaseOrderNumber,
            PurchaseRequestId = request.Id,
            ExpectedDate = seedDate.AddDays(row.ExpectedDayOffset),
            ReceivedDate = row.ReceivedDayOffset is { } offset ? seedDate.AddDays(offset) : null,
            Status = row.OrderStatus,
            CreatedAt = seedDate,
            Items =
            [
                new IncomingPurchaseOrderItem
                {
                    RawMaterialId = rawMaterial.Id,
                    OrderedQuantity = row.Quantity,
                    ReceivedQuantity = row.ReceivedQuantity
                }
            ]
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
