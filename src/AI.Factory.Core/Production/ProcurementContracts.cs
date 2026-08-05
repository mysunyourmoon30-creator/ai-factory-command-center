using System.Security.Claims;
using AI.Factory.Core.Domain;

namespace AI.Factory.Core.Production;

public sealed record SubmitPurchaseRequestCommand(byte[] RowVersion);
public sealed record ApprovePurchaseRequestCommand(byte[] RowVersion);
public sealed record RejectPurchaseRequestCommand(string RejectionReason, byte[] RowVersion);

public sealed record IncomingPurchaseOrderItemCommand(long RawMaterialId, decimal OrderedQuantity);
public sealed record CreateIncomingPurchaseOrderCommand(long PurchaseRequestId, DateTime ExpectedDate, IReadOnlyCollection<IncomingPurchaseOrderItemCommand> Items);
public sealed record SetReceivedQuantityCommand(long RawMaterialId, decimal TargetReceivedQuantity, byte[] RowVersion);

public sealed record IncomingPurchaseOrderItemDto(
    long RawMaterialId,
    string RawMaterialCode,
    string RawMaterialName,
    string Unit,
    decimal OrderedQuantity,
    decimal ReceivedQuantity);

public sealed record IncomingPurchaseOrderDto(
    long Id,
    string PurchaseOrderNumber,
    long PurchaseRequestId,
    string RequestNumber,
    DateTime ExpectedDate,
    DateTime? ReceivedDate,
    IncomingPurchaseOrderStatus Status,
    bool IsLate,
    int DelayDays,
    IReadOnlyCollection<IncomingPurchaseOrderItemDto> Items,
    byte[] RowVersion);

public interface IProcurementService
{
    Task<IReadOnlyCollection<PurchaseRequestDto>> ListPurchaseRequestsAsync(CancellationToken cancellationToken = default);
    Task<PurchaseRequestDto?> GetPurchaseRequestAsync(long id, CancellationToken cancellationToken = default);
    Task<PurchaseRequestDto?> SubmitPurchaseRequestAsync(long id, SubmitPurchaseRequestCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    Task<PurchaseRequestDto?> ApprovePurchaseRequestAsync(long id, ApprovePurchaseRequestCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    Task<PurchaseRequestDto?> RejectPurchaseRequestAsync(long id, RejectPurchaseRequestCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<IncomingPurchaseOrderDto>> ListIncomingPurchaseOrdersAsync(CancellationToken cancellationToken = default);
    Task<IncomingPurchaseOrderDto?> GetIncomingPurchaseOrderAsync(long id, CancellationToken cancellationToken = default);
    Task<IncomingPurchaseOrderDto> CreateIncomingPurchaseOrderAsync(CreateIncomingPurchaseOrderCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    Task<IncomingPurchaseOrderDto?> SetReceivedQuantityAsync(long id, SetReceivedQuantityCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
}

public static class ProcurementRules
{
    /// <summary>Open when nothing has arrived, Received once every item is fully received, Partial otherwise.</summary>
    public static IncomingPurchaseOrderStatus CalculateStatus(IEnumerable<(decimal Ordered, decimal Received)> items)
    {
        var lines = items as IReadOnlyCollection<(decimal Ordered, decimal Received)> ?? items.ToArray();
        if (lines.Count == 0 || lines.All(x => x.Received <= 0)) return IncomingPurchaseOrderStatus.Open;
        return lines.All(x => x.Received >= x.Ordered) ? IncomingPurchaseOrderStatus.Received : IncomingPurchaseOrderStatus.Partial;
    }
}
