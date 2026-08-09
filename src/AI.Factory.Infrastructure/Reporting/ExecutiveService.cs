using AI.Factory.Core.Domain;
using AI.Factory.Core.Orders;
using AI.Factory.Core.Production;
using AI.Factory.Core.Reporting;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.Factory.Infrastructure.Reporting;

/// <summary>
/// Backs the Executive Overview screen. Reuses the calculators the rest of the app already runs -
/// IOrderRiskCalculator for delivery risk, PurchaseRequestRules.IsLate for lateness, and
/// IMaterialRequirementQueryService for the locked cumulative shortage rules - so this screen can
/// never quietly disagree with the screen an executive would drill into.
/// </summary>
public sealed class ExecutiveService(
    AppDbContext dbContext,
    IOrderRiskCalculator riskCalculator,
    IMaterialRequirementQueryService requirementQuery,
    TimeProvider timeProvider) : IExecutiveService
{
    public async Task<ExecutiveOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        // Sequential, not Task.WhenAll: these share one scoped AppDbContext, which is not
        // thread-safe, so parallelising here would be a concurrency bug rather than a speed-up.
        // Same reasoning documented on Home.razor's OnInitializedAsync.
        var (pipeline, deliveryRisk) = await ReadOrdersAsync(cancellationToken);
        var procurement = await ReadProcurementFunnelAsync(cancellationToken);
        var topShortages = await ReadTopShortagesAsync(cancellationToken);
        var machines = await ReadMachinesAsync(cancellationToken);

        return new ExecutiveOverviewDto(
            timeProvider.GetUtcNow().UtcDateTime.Date,
            pipeline, deliveryRisk, procurement, topShortages, machines);
    }

    /// <summary>
    /// One pass over customer orders serves both blocks. Risk is computed by C# the provider cannot
    /// translate, so the rows have to come back anyway - deriving the pipeline counts from the same
    /// materialised set costs nothing and saves a second query.
    /// </summary>
    private async Task<(OrderPipelineDto Pipeline, DeliveryRiskDto DeliveryRisk)> ReadOrdersAsync(CancellationToken cancellationToken)
    {
        var orders = await dbContext.CustomerOrders.AsNoTracking()
            .Select(x => new
            {
                x.Status,
                x.DeliveryDate,
                PlannedCompletionDate = x.ProductionPlan != null ? x.ProductionPlan.PlannedCompletionDate : (DateTime?)null
            })
            .ToArrayAsync(cancellationToken);

        var pipeline = new OrderPipelineDto(
            orders.Count(x => x.Status == CustomerOrderStatus.Draft),
            orders.Count(x => x.Status == CustomerOrderStatus.Planned),
            orders.Count(x => x.Status == CustomerOrderStatus.InProduction),
            orders.Count(x => x.Status == CustomerOrderStatus.Completed));

        // Identical call to the one behind the dashboard's "Orders at Risk" KPI, so Warning +
        // Critical here is that KPI by construction rather than by coincidence.
        var risks = orders.Select(x => riskCalculator.Calculate(x.DeliveryDate, x.PlannedCompletionDate)).ToArray();
        var deliveryRisk = new DeliveryRiskDto(
            risks.Count(x => x == RiskStatus.Normal),
            risks.Count(x => x == RiskStatus.Warning),
            risks.Count(x => x == RiskStatus.Critical));

        return (pipeline, deliveryRisk);
    }

    private async Task<ProcurementFunnelDto> ReadProcurementFunnelAsync(CancellationToken cancellationToken)
    {
        var requestCounts = await dbContext.PurchaseRequests.AsNoTracking()
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);

        // Outstanding quantity is what separates a late order from a merely overdue-looking one, and
        // it only exists per item, so these rows are fetched rather than grouped in SQL. Both the
        // status split and the late count then come off the same pass.
        var purchaseOrders = await dbContext.IncomingPurchaseOrders.AsNoTracking()
            .Select(x => new
            {
                x.Status,
                x.ExpectedDate,
                Outstanding = x.Items.Sum(i => i.OrderedQuantity - i.ReceivedQuantity)
            })
            .ToArrayAsync(cancellationToken);

        var today = timeProvider.GetUtcNow().UtcDateTime;

        return new ProcurementFunnelDto(
            requestCounts.GetValueOrDefault(PurchaseRequestStatus.Draft),
            requestCounts.GetValueOrDefault(PurchaseRequestStatus.PendingApproval),
            requestCounts.GetValueOrDefault(PurchaseRequestStatus.Approved),
            requestCounts.GetValueOrDefault(PurchaseRequestStatus.Rejected),
            purchaseOrders.Count(x => x.Status == IncomingPurchaseOrderStatus.Open),
            purchaseOrders.Count(x => x.Status == IncomingPurchaseOrderStatus.Partial),
            purchaseOrders.Count(x => x.Status == IncomingPurchaseOrderStatus.Received),
            purchaseOrders.Count(x => PurchaseRequestRules.IsLate(x.Status, x.ExpectedDate, today, x.Outstanding)));
    }

    /// <summary>
    /// The worst shortages by quantity, from the same service /material-shortages and the CSV export
    /// read. Ordering and Take only - the cumulative demand/supply rules behind ShortageQuantity are
    /// locked and are not re-derived here.
    /// </summary>
    private async Task<IReadOnlyCollection<TopShortageDto>> ReadTopShortagesAsync(CancellationToken cancellationToken)
    {
        var materials = await requirementQuery.ListAvailabilityAsync(cancellationToken);

        return materials
            .Where(x => x.ShortageQuantity > 0)
            .OrderByDescending(x => x.ShortageQuantity)
            .ThenBy(x => x.RawMaterialCode, StringComparer.Ordinal)
            .Take(IExecutiveService.TopShortageCount)
            .Select(x => new TopShortageDto(x.RawMaterialCode, x.RawMaterialName, x.Unit, x.ShortageQuantity, x.MaterialRequiredDate))
            .ToArray();
    }

    private async Task<IReadOnlyCollection<MachineAvailabilityDto>> ReadMachinesAsync(CancellationToken cancellationToken) =>
        await dbContext.Machines.AsNoTracking()
            .OrderBy(x => x.MachineCode)
            .Select(x => new MachineAvailabilityDto(x.MachineCode, x.MachineName, x.RunningStatus, x.AlertStatus, x.LastUpdated))
            .ToArrayAsync(cancellationToken);
}
