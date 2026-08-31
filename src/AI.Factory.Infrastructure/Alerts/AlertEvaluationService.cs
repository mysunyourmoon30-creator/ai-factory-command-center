using System.Globalization;
using AI.Factory.Core.Alerts;
using AI.Factory.Core.Domain;
using AI.Factory.Core.Orders;
using AI.Factory.Core.Production;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.Factory.Infrastructure.Alerts;

public sealed class AlertEvaluationService(
    AppDbContext dbContext,
    IMaterialRequirementQueryService requirementQuery,
    IOrderRiskCalculator riskCalculator,
    TimeProvider timeProvider) : IAlertEvaluationService
{
    public async Task EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var issues = new List<AlertIssue>();
        issues.AddRange(await BuildMaterialShortageIssuesAsync(cancellationToken));
        issues.AddRange(await BuildLateProductionIssuesAsync(cancellationToken));
        issues.AddRange(await BuildLatePurchaseOrderIssuesAsync(now, cancellationToken));
        issues.AddRange(await BuildMachineIssuesAsync(cancellationToken));

        var activeAlerts = await dbContext.Alerts.Where(x => x.IsActive).ToArrayAsync(cancellationToken);

        foreach (var issue in issues)
        {
            var existing = activeAlerts.SingleOrDefault(a =>
                a.AlertType == issue.Type && a.EntityName == issue.EntityName && a.EntityId == issue.EntityId);
            if (existing is not null)
            {
                // Severity has to follow the condition, not only the wording. A machine climbing
                // from 86 to 96 degrees keeps the same dedup key (MachineTemperature/Machine/id), so
                // the alert was updated in place with the new message and the *old* severity. That
                // left a row whose badge read Warning while its own text read "(Critical)", and the
                // Dashboard sorts Critical first - so a genuinely critical machine sorted below the
                // real Critical alerts, on the screen whose job is to surface the worst thing
                // happening. The reverse direction was equally stuck, over-reporting a cleared
                // condition as Critical.
                if (existing.Severity != issue.Severity)
                {
                    existing.Severity = issue.Severity;
                }
                if (!string.Equals(existing.Message, issue.Message, StringComparison.Ordinal))
                {
                    existing.Message = issue.Message;
                }
            }
            else
            {
                dbContext.Alerts.Add(new Alert
                {
                    AlertType = issue.Type,
                    Severity = issue.Severity,
                    EntityName = issue.EntityName,
                    EntityId = issue.EntityId,
                    Message = issue.Message,
                    IsActive = true,
                    CreatedAt = now
                });
            }
        }

        var stillActiveKeys = issues.Select(x => (x.Type, x.EntityName, x.EntityId)).ToHashSet();
        foreach (var alert in activeAlerts)
        {
            if (!stillActiveKeys.Contains((alert.AlertType, alert.EntityName, alert.EntityId)))
            {
                alert.IsActive = false;
                alert.ResolvedAt = now;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed record AlertIssue(AlertType Type, string EntityName, long EntityId, AlertSeverity Severity, string Message);

    private async Task<IReadOnlyCollection<AlertIssue>> BuildMaterialShortageIssuesAsync(CancellationToken cancellationToken)
    {
        var materials = await requirementQuery.ListAvailabilityAsync(cancellationToken);
        return materials.Where(x => x.ShortageQuantity > 0)
            .Select(x => new AlertIssue(AlertType.MaterialShortage, "RawMaterial", x.RawMaterialId, AlertSeverity.Critical,
                $"{x.RawMaterialCode} is short by {x.ShortageQuantity.ToString("N3", CultureInfo.InvariantCulture)}."))
            .ToArray();
    }

    private async Task<IReadOnlyCollection<AlertIssue>> BuildLateProductionIssuesAsync(CancellationToken cancellationToken)
    {
        var orders = await dbContext.CustomerOrders.AsNoTracking()
            .Where(x => x.ProductionPlan != null)
            .Select(x => new { x.Id, x.OrderNumber, x.DeliveryDate, PlannedCompletionDate = x.ProductionPlan!.PlannedCompletionDate })
            .ToArrayAsync(cancellationToken);

        return orders
            .Where(x => riskCalculator.Calculate(x.DeliveryDate, x.PlannedCompletionDate) == RiskStatus.Critical)
            .Select(x => new AlertIssue(AlertType.LateProduction, "CustomerOrder", x.Id, AlertSeverity.Critical,
                $"{x.OrderNumber} is at Critical delivery risk."))
            .ToArray();
    }

    private async Task<IReadOnlyCollection<AlertIssue>> BuildLatePurchaseOrderIssuesAsync(DateTime today, CancellationToken cancellationToken)
    {
        var orders = await dbContext.IncomingPurchaseOrders.AsNoTracking()
            .Select(x => new { x.Id, x.PurchaseOrderNumber, x.Status, x.ExpectedDate, Outstanding = x.Items.Sum(i => i.OrderedQuantity - i.ReceivedQuantity) })
            .ToArrayAsync(cancellationToken);

        return orders
            .Where(x => PurchaseRequestRules.IsLate(x.Status, x.ExpectedDate, today, x.Outstanding))
            .Select(x => new AlertIssue(AlertType.LatePurchaseOrder, "IncomingPurchaseOrder", x.Id, AlertSeverity.Warning,
                $"{x.PurchaseOrderNumber} is late (expected {x.ExpectedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})."))
            .ToArray();
    }

    private async Task<IReadOnlyCollection<AlertIssue>> BuildMachineIssuesAsync(CancellationToken cancellationToken)
    {
        var machines = await dbContext.Machines.AsNoTracking().ToArrayAsync(cancellationToken);
        return machines.Where(x => x.AlertStatus != RiskStatus.Normal)
            .Select(x => new AlertIssue(
                x.RunningStatus == MachineRunningStatus.Stopped ? AlertType.MachineStopped : AlertType.MachineTemperature,
                "Machine",
                x.Id,
                x.AlertStatus == RiskStatus.Critical ? AlertSeverity.Critical : AlertSeverity.Warning,
                x.RunningStatus == MachineRunningStatus.Stopped
                    ? $"{x.MachineCode} is stopped."
                    : $"{x.MachineCode} is running at {x.Temperature.ToString("N1", CultureInfo.InvariantCulture)}°C ({x.AlertStatus})."))
            .ToArray();
    }
}
