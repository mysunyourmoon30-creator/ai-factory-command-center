using AI.Factory.Core.Alerts;
using AI.Factory.Core.Domain;
using AI.Factory.Core.Orders;
using AI.Factory.Core.Production;
using AI.Factory.Core.Reporting;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.Factory.Infrastructure.Reporting;

/// <summary>
/// Every KPI is read live from the database (Master Scope V4 Module 8): no hard-coded figures,
/// no AI-computed numbers. Reuses IMaterialRequirementQueryService exactly as Day 7's
/// MaterialShortageService already does, rather than re-deriving the cumulative shortage rules.
/// </summary>
public sealed class DashboardService(
    AppDbContext dbContext,
    IOrderRiskCalculator riskCalculator,
    IMaterialRequirementQueryService requirementQuery,
    IAlertEvaluationService alertEvaluation,
    TimeProvider timeProvider) : IDashboardService
{
    public async Task<DashboardKpiDto> GetKpiAsync(CancellationToken cancellationToken = default)
    {
        var customerOrderCount = await dbContext.CustomerOrders.CountAsync(cancellationToken);
        var productionPlanCount = await dbContext.ProductionPlans.CountAsync(cancellationToken);
        var (ordersAtRisk, criticalOrders) = await ComputeOrderRiskCountsAsync(cancellationToken);
        var shortageCount = await CountMaterialShortagesAsync(cancellationToken);
        var latePurchaseOrderCount = await CountLatePurchaseOrdersAsync(cancellationToken);
        var machineAlertCount = await dbContext.Machines.CountAsync(x => x.AlertStatus != RiskStatus.Normal, cancellationToken);
        var criticalMachineCount = await dbContext.Machines.CountAsync(x => x.AlertStatus == RiskStatus.Critical, cancellationToken);

        return new DashboardKpiDto(
            customerOrderCount, productionPlanCount, ordersAtRisk, shortageCount,
            latePurchaseOrderCount, machineAlertCount, criticalOrders + criticalMachineCount);
    }

    /// <summary>Matches the locked GetDailyFactorySummary() shape (§16.11).</summary>
    public async Task<DailySummaryDto> GetDailySummaryAsync(CancellationToken cancellationToken = default)
    {
        var customerOrderCount = await dbContext.CustomerOrders.CountAsync(cancellationToken);
        var productionPlanCount = await dbContext.ProductionPlans.CountAsync(cancellationToken);
        var shortageCount = await CountMaterialShortagesAsync(cancellationToken);
        var latePurchaseOrderCount = await CountLatePurchaseOrdersAsync(cancellationToken);
        var criticalMachineCount = await dbContext.Machines.CountAsync(x => x.AlertStatus == RiskStatus.Critical, cancellationToken);

        return new DailySummaryDto(Today().Date, customerOrderCount, productionPlanCount, shortageCount, latePurchaseOrderCount, criticalMachineCount);
    }

    public async Task<CriticalRisksDto> GetCriticalRisksAsync(CancellationToken cancellationToken = default)
    {
        var orders = await dbContext.CustomerOrders.AsNoTracking()
            .Where(x => x.ProductionPlan != null)
            .Select(x => new { x.OrderNumber, x.DeliveryDate, PlannedCompletionDate = x.ProductionPlan!.PlannedCompletionDate })
            .ToArrayAsync(cancellationToken);

        var delayedOrders = orders
            .Where(x => riskCalculator.Calculate(x.DeliveryDate, x.PlannedCompletionDate) == RiskStatus.Critical)
            .OrderBy(x => x.OrderNumber)
            .Select(x => new DelayedProductionOrderDto(
                x.OrderNumber, x.DeliveryDate, x.PlannedCompletionDate,
                Math.Max((x.PlannedCompletionDate.Date - x.DeliveryDate.Date).Days, 0)))
            .ToArray();

        var criticalMachines = await dbContext.Machines.AsNoTracking()
            .Where(x => x.AlertStatus == RiskStatus.Critical)
            .OrderBy(x => x.MachineCode)
            .Select(x => new CriticalMachineDto(x.MachineCode, x.Temperature, x.Speed))
            .ToArrayAsync(cancellationToken);

        return new CriticalRisksDto(delayedOrders, criticalMachines);
    }

    /// <summary>
    /// Evaluates alerts before reading, so a screen refresh is the read-side trigger the locked
    /// dedup rule assumes ("refreshing the screen must not create a duplicate active alert").
    /// </summary>
    public async Task<IReadOnlyCollection<ActiveAlertDto>> ListActiveAlertsAsync(CancellationToken cancellationToken = default)
    {
        await alertEvaluation.EvaluateAsync(cancellationToken);

        return await dbContext.Alerts.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new ActiveAlertDto(x.Id, x.AlertType, x.Severity, x.EntityName, x.EntityId, x.Message, x.CreatedAt))
            .ToArrayAsync(cancellationToken);
    }

    private async Task<(int AtRisk, int Critical)> ComputeOrderRiskCountsAsync(CancellationToken cancellationToken)
    {
        var orders = await dbContext.CustomerOrders.AsNoTracking()
            .Select(x => new { x.DeliveryDate, PlannedCompletionDate = x.ProductionPlan != null ? x.ProductionPlan.PlannedCompletionDate : (DateTime?)null })
            .ToArrayAsync(cancellationToken);
        var risks = orders.Select(x => riskCalculator.Calculate(x.DeliveryDate, x.PlannedCompletionDate)).ToArray();
        return (risks.Count(x => x != RiskStatus.Normal), risks.Count(x => x == RiskStatus.Critical));
    }

    private async Task<int> CountMaterialShortagesAsync(CancellationToken cancellationToken) =>
        (await requirementQuery.ListAvailabilityAsync(cancellationToken)).Count(x => x.ShortageQuantity > 0);

    private async Task<int> CountLatePurchaseOrdersAsync(CancellationToken cancellationToken)
    {
        var today = Today();
        var orders = await dbContext.IncomingPurchaseOrders.AsNoTracking()
            .Select(x => new { x.Status, x.ExpectedDate, Outstanding = x.Items.Sum(i => i.OrderedQuantity - i.ReceivedQuantity) })
            .ToArrayAsync(cancellationToken);
        return orders.Count(x => PurchaseRequestRules.IsLate(x.Status, x.ExpectedDate, today, x.Outstanding));
    }

    private DateTime Today() => timeProvider.GetUtcNow().UtcDateTime;
}
