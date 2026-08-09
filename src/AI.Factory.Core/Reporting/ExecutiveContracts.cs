using AI.Factory.Core.Domain;

namespace AI.Factory.Core.Reporting;

/// <summary>
/// Executive Overview (screen 12). Read-only, and deliberately answers only questions the Dashboard
/// does not: it breaks down figures the Dashboard reports as single totals and adds the procurement
/// funnel, which has no number on the Dashboard at all.
///
/// Every value here is an existing calculation grouped or sorted for display - no new business rule,
/// no new column, no new table. The Dashboard keeps sole ownership of the alert list and the
/// critical-risk tables; duplicating them here is exactly what would make this screen worthless.
///
/// Two things an executive screen normally shows are absent because the schema cannot support them,
/// not because they were forgotten: money (no entity carries a price, cost, or amount) and
/// trend-over-time (no snapshot history exists, and adding one would be a 15th table).
/// </summary>
public sealed record ExecutiveOverviewDto(
    DateTime AsOfDate,
    OrderPipelineDto Pipeline,
    DeliveryRiskDto DeliveryRisk,
    ProcurementFunnelDto Procurement,
    IReadOnlyCollection<TopShortageDto> TopShortages,
    IReadOnlyCollection<MachineAvailabilityDto> Machines);

/// <summary>Customer orders by lifecycle status - where work is piling up.</summary>
public sealed record OrderPipelineDto(int Draft, int Planned, int InProduction, int Completed)
{
    public int Total => Draft + Planned + InProduction + Completed;
}

/// <summary>
/// The same <c>IOrderRiskCalculator</c> result the Dashboard collapses into one
/// "Orders at Risk" figure, split into its three buckets. <c>AtRisk</c> is defined to match that
/// KPI exactly (everything that is not Normal), so the two screens cannot disagree.
/// </summary>
public sealed record DeliveryRiskDto(int Normal, int Warning, int Critical)
{
    public int Total => Normal + Warning + Critical;
    public int AtRisk => Warning + Critical;
}

/// <summary>
/// Purchase requests and incoming purchase orders by status. <c>RequestsPendingApproval</c> is the
/// figure this screen exists for: approving requests is the Manager role's actual job per
/// CanApprovePurchaseRequest, and no existing screen puts a number on it.
/// </summary>
public sealed record ProcurementFunnelDto(
    int RequestsDraft,
    int RequestsPendingApproval,
    int RequestsApproved,
    int RequestsRejected,
    int PurchaseOrdersOpen,
    int PurchaseOrdersPartial,
    int PurchaseOrdersReceived,
    int PurchaseOrdersLate);

public sealed record TopShortageDto(
    string RawMaterialCode,
    string RawMaterialName,
    string Unit,
    decimal ShortageQuantity,
    DateTime? MaterialRequiredDate);

/// <summary>
/// Slimmer than <c>MachineDto</c> on purpose: that record carries RowVersion, which is a
/// concurrency token for the simulator write path and has no business reaching a read-only screen.
/// </summary>
public sealed record MachineAvailabilityDto(
    string MachineCode,
    string MachineName,
    MachineRunningStatus RunningStatus,
    RiskStatus AlertStatus,
    DateTime LastUpdated);

/// <summary>
/// No <c>ClaimsPrincipal actor</c> parameter, matching <see cref="IDashboardService" />: the service
/// only reads, so there is no mutation for an actor re-check to guard.
/// </summary>
public interface IExecutiveService
{
    /// <summary>How many shortage rows the overview carries - the worst offenders, not the full list.</summary>
    const int TopShortageCount = 5;

    Task<ExecutiveOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default);
}
