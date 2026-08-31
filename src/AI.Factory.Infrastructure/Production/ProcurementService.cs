using System.Globalization;
using System.Security.Claims;
using AI.Factory.Core.Domain;
using AI.Factory.Core.MasterData;
using AI.Factory.Core.Production;
using AI.Factory.Core.Security;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.Factory.Infrastructure.Production;

public sealed class ProcurementService(AppDbContext dbContext, IAuditWriter auditWriter, TimeProvider timeProvider) : IProcurementService
{
    public async Task<IReadOnlyCollection<PurchaseRequestDto>> ListPurchaseRequestsAsync(CancellationToken cancellationToken = default) =>
        (await PurchaseRequestQuery().OrderBy(x => x.RequestedDate).ThenBy(x => x.RequestNumber).ToArrayAsync(cancellationToken))
            .Select(PurchaseRequestMapper.Map).ToArray();

    public async Task<PurchaseRequestDto?> GetPurchaseRequestAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await PurchaseRequestQuery().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity is null ? null : PurchaseRequestMapper.Map(entity);
    }

    public async Task<PurchaseRequestDto?> SubmitPurchaseRequestAsync(long id, SubmitPurchaseRequestCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        EnsureCanSubmit(actor);
        var request = await dbContext.PurchaseRequests.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (request is null) return null;
        EnsureRowVersion(request.RowVersion, command.RowVersion);
        if (request.Status != PurchaseRequestStatus.Draft)
            throw new DomainValidationException($"Only a Draft purchase request can be submitted; current status is {request.Status}.");

        dbContext.Entry(request).Property(x => x.RowVersion).OriginalValue = command.RowVersion;
        request.Status = PurchaseRequestStatus.PendingApproval;
        await SavePurchaseRequestAsync(cancellationToken);
        await auditWriter.WriteAsync("Submit Purchase Request", nameof(PurchaseRequest), request.Id, "Success", cancellationToken: cancellationToken);
        return await GetPurchaseRequestAsync(request.Id, cancellationToken);
    }

    public async Task<PurchaseRequestDto?> ApprovePurchaseRequestAsync(long id, ApprovePurchaseRequestCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        EnsureCanApprove(actor);
        var request = await dbContext.PurchaseRequests.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (request is null) return null;
        EnsureRowVersion(request.RowVersion, command.RowVersion);
        if (request.Status != PurchaseRequestStatus.PendingApproval)
            throw new DomainValidationException($"Only a PendingApproval purchase request can be approved; current status is {request.Status}.");

        dbContext.Entry(request).Property(x => x.RowVersion).OriginalValue = command.RowVersion;
        request.Status = PurchaseRequestStatus.Approved;
        request.ApprovedByUserId = CurrentUserId(actor);
        request.ApprovedDate = Today();
        await SavePurchaseRequestAsync(cancellationToken);
        await auditWriter.WriteAsync("Approve Purchase Request", nameof(PurchaseRequest), request.Id, "Success", cancellationToken: cancellationToken);
        return await GetPurchaseRequestAsync(request.Id, cancellationToken);
    }

    public async Task<PurchaseRequestDto?> RejectPurchaseRequestAsync(long id, RejectPurchaseRequestCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        EnsureCanApprove(actor);
        if (string.IsNullOrWhiteSpace(command.RejectionReason) || command.RejectionReason.Trim().Length > 500)
            throw new DomainValidationException("A rejection reason is required and limited to 500 characters.");
        var request = await dbContext.PurchaseRequests.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (request is null) return null;
        EnsureRowVersion(request.RowVersion, command.RowVersion);
        if (request.Status != PurchaseRequestStatus.PendingApproval)
            throw new DomainValidationException($"Only a PendingApproval purchase request can be rejected; current status is {request.Status}.");

        dbContext.Entry(request).Property(x => x.RowVersion).OriginalValue = command.RowVersion;
        request.Status = PurchaseRequestStatus.Rejected;
        request.RejectionReason = command.RejectionReason.Trim();
        await SavePurchaseRequestAsync(cancellationToken);
        await auditWriter.WriteAsync("Reject Purchase Request", nameof(PurchaseRequest), request.Id, "Success", cancellationToken: cancellationToken);
        return await GetPurchaseRequestAsync(request.Id, cancellationToken);
    }

    public async Task<IReadOnlyCollection<IncomingPurchaseOrderDto>> ListIncomingPurchaseOrdersAsync(CancellationToken cancellationToken = default) =>
        (await IncomingPurchaseOrderQuery().OrderBy(x => x.ExpectedDate).ThenBy(x => x.PurchaseOrderNumber).ToArrayAsync(cancellationToken))
            .Select(Map).ToArray();

    public async Task<IncomingPurchaseOrderDto?> GetIncomingPurchaseOrderAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await IncomingPurchaseOrderQuery().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task<IncomingPurchaseOrderDto> CreateIncomingPurchaseOrderAsync(CreateIncomingPurchaseOrderCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        EnsureCanRecordIncomingPurchaseOrder(actor);
        Validate(command);

        var purchaseRequest = await dbContext.PurchaseRequests.AsNoTracking()
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == command.PurchaseRequestId, cancellationToken)
            ?? throw new DomainValidationException("Purchase request was not found.");
        if (purchaseRequest.Status != PurchaseRequestStatus.Approved)
            throw new DomainValidationException("Only an Approved purchase request can raise an incoming purchase order.");
        if (await dbContext.IncomingPurchaseOrders.AnyAsync(x => x.PurchaseRequestId == command.PurchaseRequestId, cancellationToken))
            throw new BusinessConflictException("An incoming purchase order already exists for this purchase request.");

        var items = command.Items.Select(item =>
        {
            var requested = purchaseRequest.Items.SingleOrDefault(x => x.RawMaterialId == item.RawMaterialId)
                ?? throw new DomainValidationException("Every raw material must be part of the approved purchase request.");
            if (item.OrderedQuantity > requested.RequestedQuantity)
                throw new DomainValidationException($"Ordered quantity may not exceed the requested quantity of {requested.RequestedQuantity.ToString("N3", CultureInfo.InvariantCulture)}.");
            return new IncomingPurchaseOrderItem { RawMaterialId = item.RawMaterialId, OrderedQuantity = item.OrderedQuantity, ReceivedQuantity = 0 };
        }).ToArray();

        var order = new IncomingPurchaseOrder
        {
            PurchaseOrderNumber = await NextPurchaseOrderNumberAsync(cancellationToken),
            PurchaseRequestId = command.PurchaseRequestId,
            ExpectedDate = NormalizeDate(command.ExpectedDate),
            Status = IncomingPurchaseOrderStatus.Open,
            CreatedAt = Today(),
            Items = items
        };

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            dbContext.IncomingPurchaseOrders.Add(order);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);

            // Two different unique indexes reach this catch, and reporting the first for both was
            // wrong. IX_IncomingPurchaseOrders_PurchaseRequestId means a genuine duplicate - though
            // the pre-check above already handles the non-racing case. IX_..._PurchaseOrderNumber
            // means two concurrent creates for *different* requests both numbered themselves off the
            // same COUNT(*): NextPurchaseOrderNumberAsync runs before this transaction opens, and
            // this transaction is ReadCommitted, so nothing serialises them. The caller was then
            // told an order already existed for their purchase request when none did, sending them
            // to look at the wrong thing instead of simply retrying.
            var duplicateForThisRequest = await dbContext.IncomingPurchaseOrders.AsNoTracking()
                .AnyAsync(x => x.PurchaseRequestId == command.PurchaseRequestId, cancellationToken);
            throw new BusinessConflictException(duplicateForThisRequest
                ? "An incoming purchase order already exists for this purchase request."
                : "Another incoming purchase order was created at the same moment and took this order number. Please try again.");
        }

        await auditWriter.WriteAsync("Create Incoming Purchase Order", nameof(IncomingPurchaseOrder), order.Id, "Success", cancellationToken: cancellationToken);
        return (await GetIncomingPurchaseOrderAsync(order.Id, cancellationToken))!;
    }

    /// <summary>
    /// The command carries the new cumulative Received Quantity, not an increment, so a resend of
    /// the same target is a no-op: Delta is 0 and stock is left unchanged (locked Test 12).
    /// </summary>
    public async Task<IncomingPurchaseOrderDto?> SetReceivedQuantityAsync(long id, SetReceivedQuantityCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        EnsureCanRecordIncomingPurchaseOrder(actor);
        if (command.TargetReceivedQuantity < 0) throw new DomainValidationException("Received quantity may not be negative.");

        var order = await dbContext.IncomingPurchaseOrders
            .Include(x => x.Items).ThenInclude(x => x.RawMaterial)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (order is null) return null;
        EnsureRowVersion(order.RowVersion, command.RowVersion);
        var item = order.Items.SingleOrDefault(x => x.RawMaterialId == command.RawMaterialId)
            ?? throw new DomainValidationException("This raw material is not part of the incoming purchase order.");
        if (command.TargetReceivedQuantity < item.ReceivedQuantity)
            throw new DomainValidationException("Received quantity may not decrease.");
        if (command.TargetReceivedQuantity > item.OrderedQuantity)
            throw new DomainValidationException($"Received quantity may not exceed the ordered quantity of {item.OrderedQuantity.ToString("N3", CultureInfo.InvariantCulture)}.");

        var now = Today();
        var delta = command.TargetReceivedQuantity - item.ReceivedQuantity;
        item.RawMaterial.CurrentStock += delta;
        item.RawMaterial.UpdatedAt = now;
        item.ReceivedQuantity = command.TargetReceivedQuantity;

        var status = ProcurementRules.CalculateStatus(order.Items.Select(x => (x.OrderedQuantity, x.ReceivedQuantity)));
        order.Status = status;
        if (status == IncomingPurchaseOrderStatus.Received) order.ReceivedDate ??= now;

        dbContext.Entry(order).Property(x => x.RowVersion).OriginalValue = command.RowVersion;
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException("Incoming purchase order changed; reload before saving."); }

        await auditWriter.WriteAsync("Receive Incoming Purchase Order", nameof(IncomingPurchaseOrder), order.Id, $"Success: {order.Status}", cancellationToken: cancellationToken);
        return await GetIncomingPurchaseOrderAsync(order.Id, cancellationToken);
    }

    private IQueryable<PurchaseRequest> PurchaseRequestQuery() => dbContext.PurchaseRequests.AsNoTracking()
        .Include(x => x.SourceProductionPlan)
        .Include(x => x.IncomingPurchaseOrder)
        .Include(x => x.Items).ThenInclude(x => x.RawMaterial);

    private IQueryable<IncomingPurchaseOrder> IncomingPurchaseOrderQuery() => dbContext.IncomingPurchaseOrders.AsNoTracking()
        .Include(x => x.PurchaseRequest)
        .Include(x => x.Items).ThenInclude(x => x.RawMaterial);

    private IncomingPurchaseOrderDto Map(IncomingPurchaseOrder order)
    {
        var today = Today();
        var outstanding = order.Items.Sum(x => MaterialAvailabilityRules.CalculateOutstandingQuantity(x.OrderedQuantity, x.ReceivedQuantity));
        return new IncomingPurchaseOrderDto(
            order.Id,
            order.PurchaseOrderNumber,
            order.PurchaseRequestId,
            order.PurchaseRequest.RequestNumber,
            order.ExpectedDate,
            order.ReceivedDate,
            order.Status,
            PurchaseRequestRules.IsLate(order.Status, order.ExpectedDate, today, outstanding),
            PurchaseRequestRules.CalculateDelayDays(order.ExpectedDate, today),
            order.Items.OrderBy(x => x.RawMaterial.Code)
                .Select(x => new IncomingPurchaseOrderItemDto(x.RawMaterialId, x.RawMaterial.Code, x.RawMaterial.Name, x.RawMaterial.Unit, x.OrderedQuantity, x.ReceivedQuantity))
                .ToArray(),
            order.RowVersion);
    }

    private async Task SavePurchaseRequestAsync(CancellationToken cancellationToken)
    {
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException("Purchase request changed; reload before saving."); }
    }

    private async Task<string> NextPurchaseOrderNumberAsync(CancellationToken cancellationToken)
    {
        var count = await dbContext.IncomingPurchaseOrders.CountAsync(cancellationToken);
        return $"PO-{count + 1:D6}";
    }

    private static void Validate(CreateIncomingPurchaseOrderCommand command)
    {
        if (command.PurchaseRequestId <= 0) throw new DomainValidationException("Purchase request is required.");
        if (command.ExpectedDate == default) throw new DomainValidationException("Expected date is required.");
        if (command.Items is null || command.Items.Count == 0) throw new DomainValidationException("At least one item is required.");
        if (command.Items.Any(x => x.RawMaterialId <= 0 || x.OrderedQuantity <= 0))
            throw new DomainValidationException("Every item requires a raw material and a positive ordered quantity.");
    }

    private static void EnsureRowVersion(byte[] current, byte[] submitted)
    {
        if (submitted is null || !current.SequenceEqual(submitted))
            throw new ConcurrencyConflictException("This record changed; reload before saving.");
    }

    private static void EnsureCanSubmit(ClaimsPrincipal actor)
    {
        if (!actor.IsInRole(RoleNames.Admin) && !actor.IsInRole(RoleNames.Manager) && !actor.IsInRole(RoleNames.Planner))
            throw new UnauthorizedAccessException("Admin, Manager, or Planner role is required to submit a purchase request.");
    }

    private static void EnsureCanApprove(ClaimsPrincipal actor)
    {
        if (!actor.IsInRole(RoleNames.Admin) && !actor.IsInRole(RoleNames.Manager))
            throw new UnauthorizedAccessException("Admin or Manager role is required to approve or reject a purchase request.");
    }

    private static void EnsureCanRecordIncomingPurchaseOrder(ClaimsPrincipal actor)
    {
        if (!actor.IsInRole(RoleNames.Admin) && !actor.IsInRole(RoleNames.Manager))
            throw new UnauthorizedAccessException("Admin or Manager role is required to record an incoming purchase order.");
    }

    private static long CurrentUserId(ClaimsPrincipal actor)
    {
        var value = actor.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(value, out var id) ? id : throw new DomainValidationException("An authenticated user is required.");
    }

    private static DateTime NormalizeDate(DateTime value) => DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
    private DateTime Today() => timeProvider.GetUtcNow().UtcDateTime;
}
