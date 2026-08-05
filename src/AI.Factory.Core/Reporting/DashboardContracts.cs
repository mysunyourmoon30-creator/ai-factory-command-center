using AI.Factory.Core.Domain;

namespace AI.Factory.Core.Reporting;

public sealed record DashboardKpiDto(
    int CustomerOrderCount,
    int ProductionPlanCount,
    int OrdersAtRiskCount,
    int MaterialShortageCount,
    int LatePurchaseOrderCount,
    int MachineAlertCount,
    int CriticalRiskCount);

/// <summary>Matches the locked GetDailyFactorySummary() shape (Master Scope V4 §16.11).</summary>
public sealed record DailySummaryDto(
    DateTime AsOfDate,
    int CustomerOrderCount,
    int ProductionPlanCount,
    int MaterialShortageCount,
    int LatePurchaseOrderCount,
    int CriticalMachineCount);

public sealed record DelayedProductionOrderDto(string OrderNumber, DateTime DeliveryDate, DateTime PlannedCompletionDate, int DelayRiskDays);
public sealed record CriticalMachineDto(string MachineCode, decimal Temperature, decimal Speed);
public sealed record CriticalRisksDto(IReadOnlyCollection<DelayedProductionOrderDto> DelayedOrders, IReadOnlyCollection<CriticalMachineDto> CriticalMachines);
public sealed record ActiveAlertDto(long Id, AlertType AlertType, AlertSeverity Severity, string EntityName, long EntityId, string Message, DateTime CreatedAt);

public interface IDashboardService
{
    Task<DashboardKpiDto> GetKpiAsync(CancellationToken cancellationToken = default);
    Task<DailySummaryDto> GetDailySummaryAsync(CancellationToken cancellationToken = default);
    Task<CriticalRisksDto> GetCriticalRisksAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ActiveAlertDto>> ListActiveAlertsAsync(CancellationToken cancellationToken = default);
}
