using System.Security.Claims;
using AI.Factory.Core.Domain;

namespace AI.Factory.Core.Production;

public sealed record AffectedOrderDto(
    long CustomerOrderId,
    string OrderNumber,
    string CustomerName,
    long ProductionPlanId,
    string PlanNumber,
    DateTime RequiredDate,
    decimal RequiredQuantity);

public sealed record LatePurchaseOrderDto(
    long IncomingPurchaseOrderId,
    string PurchaseOrderNumber,
    DateTime ExpectedDate,
    int DelayDays,
    decimal OutstandingQuantity);

public sealed record MaterialShortageDto(
    long RawMaterialId,
    string RawMaterialCode,
    string RawMaterialName,
    string Unit,
    decimal TotalRequirement,
    decimal OnHandAvailable,
    decimal EligibleIncoming,
    decimal ProjectedAvailable,
    decimal ShortageQuantity,
    DateTime? MaterialRequiredDate,
    DateTime? EvaluationDate,
    IReadOnlyCollection<AffectedOrderDto> AffectedOrders,
    IReadOnlyCollection<LatePurchaseOrderDto> LatePurchaseOrders);

public sealed record PurchaseRequestItemDto(long RawMaterialId, string RawMaterialCode, decimal RequestedQuantity, DateTime ExpectedDate);

public sealed record PurchaseRequestDto(
    long Id,
    string RequestNumber,
    long SourceProductionPlanId,
    string PlanNumber,
    PurchaseRequestStatus Status,
    DateTime RequestedDate,
    DateTime? ApprovedDate,
    string? RejectionReason,
    bool HasIncomingPurchaseOrder,
    IReadOnlyCollection<PurchaseRequestItemDto> Items,
    byte[] RowVersion);

public sealed record CreatePurchaseRequestCommand(
    long RawMaterialId,
    long SourceProductionPlanId,
    decimal RequestedQuantity,
    DateTime ExpectedDate);

public interface IMaterialShortageService
{
    Task<IReadOnlyCollection<MaterialShortageDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<MaterialShortageDto?> GetAsync(long rawMaterialId, CancellationToken cancellationToken = default);
    Task<PurchaseRequestDto> CreatePurchaseRequestAsync(CreatePurchaseRequestCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
}

public static class PurchaseRequestRules
{
    /// <summary>Statuses that make an existing request block a new one for the same plan and material.</summary>
    public static readonly PurchaseRequestStatus[] ActiveStatuses =
        [PurchaseRequestStatus.Draft, PurchaseRequestStatus.PendingApproval];

    public static bool IsActive(PurchaseRequestStatus status) => ActiveStatuses.Contains(status);

    /// <summary>A purchase order is late once its expected date has passed while quantity is outstanding.</summary>
    public static bool IsLate(IncomingPurchaseOrderStatus status, DateTime expectedDate, DateTime today, decimal outstandingQuantity) =>
        status != IncomingPurchaseOrderStatus.Received && outstandingQuantity > 0 && expectedDate.Date < today.Date;

    public static int CalculateDelayDays(DateTime expectedDate, DateTime today) =>
        Math.Max((today.Date - expectedDate.Date).Days, 0);
}
