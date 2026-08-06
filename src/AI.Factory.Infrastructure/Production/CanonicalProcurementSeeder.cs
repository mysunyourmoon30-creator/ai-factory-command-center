using AI.Factory.Core.Domain;
using AI.Factory.Infrastructure.Identity;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Factory.Infrastructure.Production;

/// <summary>
/// Seeds the locked spec's "Existing Purchase Request 1" / "Existing Incoming PO 1" baseline.
/// PR-BASE-001 is Approved (not Draft/PendingApproval) so it never blocks a new PR for the same
/// plan+material, and PO-BASE-001's ExpectedDate is set after PP-DEMO-001's Required Date so it
/// is excluded from Cumulative Incoming at that date - preserving the locked 1,250 kg shortage
/// figure - while also being non-late relative to T, preserving LatePurchaseOrderCount = 0.
/// </summary>
public static class CanonicalProcurementSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var db = services.GetRequiredService<AppDbContext>();
        if (await db.PurchaseRequests.AnyAsync(x => x.RequestNumber == "PR-BASE-001", cancellationToken))
            return;

        var anchor = await db.CustomerOrders.AsNoTracking().SingleOrDefaultAsync(x => x.OrderNumber == "SO-DEMO-001", cancellationToken)
            ?? throw new InvalidOperationException("Seed canonical customer orders before procurement.");
        var seedDate = DateTime.SpecifyKind(anchor.CreatedAt.Date, DateTimeKind.Utc);

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var planner = await userManager.FindByNameAsync("planner.demo")
            ?? throw new InvalidOperationException("Seed identity before procurement.");
        var manager = await userManager.FindByNameAsync("manager.demo")
            ?? throw new InvalidOperationException("Seed identity before procurement.");

        var plan = await db.ProductionPlans.AsNoTracking().SingleOrDefaultAsync(x => x.PlanNumber == "PP-DEMO-001", cancellationToken)
            ?? throw new InvalidOperationException("Seed canonical production plans before procurement.");
        var rawMaterial = await db.RawMaterials.AsNoTracking().SingleAsync(x => x.Code == "RM-001", cancellationToken);

        var request = new PurchaseRequest
        {
            RequestNumber = "PR-BASE-001",
            SourceProductionPlanId = plan.Id,
            Status = PurchaseRequestStatus.Approved,
            RequestedByUserId = planner.Id,
            RequestedDate = seedDate,
            ApprovedByUserId = manager.Id,
            ApprovedDate = seedDate,
            CreatedAt = seedDate,
            Items =
            [
                new PurchaseRequestItem
                {
                    RawMaterialId = rawMaterial.Id,
                    RequestedQuantity = 500,
                    ExpectedDate = seedDate.AddDays(10)
                }
            ]
        };
        db.PurchaseRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);

        db.IncomingPurchaseOrders.Add(new IncomingPurchaseOrder
        {
            PurchaseOrderNumber = "PO-BASE-001",
            PurchaseRequestId = request.Id,
            ExpectedDate = seedDate.AddDays(10),
            Status = IncomingPurchaseOrderStatus.Open,
            CreatedAt = seedDate,
            Items =
            [
                new IncomingPurchaseOrderItem
                {
                    RawMaterialId = rawMaterial.Id,
                    OrderedQuantity = 500,
                    ReceivedQuantity = 0
                }
            ]
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
