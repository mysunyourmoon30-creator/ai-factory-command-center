using AI.Factory.Core.Domain;
using AI.Factory.Core.Production;

namespace AI.Factory.Infrastructure.Production;

/// <summary>
/// Shared PurchaseRequest -> PurchaseRequestDto projection. The query must eager-load
/// SourceProductionPlan, IncomingPurchaseOrder, and Items.RawMaterial before mapping.
/// </summary>
internal static class PurchaseRequestMapper
{
    internal static PurchaseRequestDto Map(PurchaseRequest request) => new(
        request.Id,
        request.RequestNumber,
        request.SourceProductionPlanId,
        request.SourceProductionPlan.PlanNumber,
        request.Status,
        request.RequestedDate,
        request.ApprovedDate,
        request.RejectionReason,
        request.IncomingPurchaseOrder is not null,
        request.Items.OrderBy(x => x.RawMaterial.Code)
            .Select(x => new PurchaseRequestItemDto(x.RawMaterialId, x.RawMaterial.Code, x.RequestedQuantity, x.ExpectedDate))
            .ToArray(),
        request.RowVersion);
}
