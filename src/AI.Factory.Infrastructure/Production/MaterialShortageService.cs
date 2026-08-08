using System.Data;
using System.Globalization;
using System.Security.Claims;
using AI.Factory.Core.Domain;
using AI.Factory.Core.MasterData;
using AI.Factory.Core.Production;
using AI.Factory.Core.Security;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.Factory.Infrastructure.Production;

public sealed class MaterialShortageService(
    AppDbContext dbContext,
    IMaterialRequirementQueryService requirementQuery,
    IAuditWriter auditWriter,
    TimeProvider timeProvider) : IMaterialShortageService
{
    public async Task<IReadOnlyCollection<MaterialShortageDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var availability = await requirementQuery.ListAvailabilityAsync(cancellationToken);
        var withDemand = availability.Where(x => x.EvaluationDate is not null).ToArray();
        if (withDemand.Length == 0) return [];

        var materialIds = withDemand.Select(x => x.RawMaterialId).ToArray();
        var affected = await LoadAffectedOrdersAsync(materialIds, cancellationToken);
        var late = await LoadLatePurchaseOrdersAsync(materialIds, cancellationToken);

        return withDemand.Select(x => Map(x, affected, late)).ToArray();
    }

    public async Task<MaterialShortageDto?> GetAsync(long rawMaterialId, CancellationToken cancellationToken = default)
    {
        var availability = (await requirementQuery.ListAvailabilityAsync(cancellationToken))
            .SingleOrDefault(x => x.RawMaterialId == rawMaterialId);
        if (availability is null || availability.EvaluationDate is null) return null;

        long[] materialIds = [rawMaterialId];
        return Map(
            availability,
            await LoadAffectedOrdersAsync(materialIds, cancellationToken),
            await LoadLatePurchaseOrdersAsync(materialIds, cancellationToken));
    }

    /// <summary>
    /// Recomputes the shortage and re-checks for a competing active request inside a Serializable
    /// transaction, so two callers racing on the same plan and material cannot both insert.
    /// </summary>
    public async Task<PurchaseRequestDto> CreatePurchaseRequestAsync(
        CreatePurchaseRequestCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        EnsureCanCreate(actor);
        Validate(command);

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var plan = await dbContext.ProductionPlans.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == command.SourceProductionPlanId, cancellationToken)
                ?? throw new DomainValidationException("Production plan was not found.");
            if (!MaterialAvailabilityRules.IsActiveDemand(plan.Status))
                throw new DomainValidationException("Only a Planned or InProduction plan can raise a purchase request.");
            if (!await dbContext.MaterialRequirements.AnyAsync(
                    x => x.ProductionPlanId == plan.Id && x.RawMaterialId == command.RawMaterialId, cancellationToken))
                throw new DomainValidationException("The production plan has no requirement for this raw material.");

            var shortage = await RecalculateShortageAsync(command.RawMaterialId, cancellationToken);
            if (shortage <= 0)
                throw new BusinessConflictException("There is no outstanding shortage for this raw material.");
            if (command.RequestedQuantity > shortage)
                throw new DomainValidationException($"Requested quantity may not exceed the current shortage of {shortage.ToString("N3", CultureInfo.InvariantCulture)}.");

            if (await HasActiveRequestAsync(command.SourceProductionPlanId, command.RawMaterialId, cancellationToken))
                throw new BusinessConflictException("An active purchase request already exists for this plan and raw material.");

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var request = new PurchaseRequest
            {
                RequestNumber = await NextRequestNumberAsync(cancellationToken),
                SourceProductionPlanId = command.SourceProductionPlanId,
                Status = PurchaseRequestStatus.Draft,
                RequestedByUserId = CurrentUserId(actor),
                RequestedDate = now,
                CreatedAt = now,
                Items =
                [
                    new PurchaseRequestItem
                    {
                        RawMaterialId = command.RawMaterialId,
                        RequestedQuantity = command.RequestedQuantity,
                        ExpectedDate = NormalizeDate(command.ExpectedDate)
                    }
                ]
            };

            dbContext.PurchaseRequests.Add(request);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);

            await auditWriter.WriteAsync("Create Purchase Request", nameof(PurchaseRequest), request.Id, "Success", cancellationToken: cancellationToken);
            return (await GetPurchaseRequestAsync(request.Id, cancellationToken))!;
        }
        catch (DbUpdateException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            throw new BusinessConflictException("An active purchase request already exists for this plan and raw material.");
        }
    }

    private async Task<decimal> RecalculateShortageAsync(long rawMaterialId, CancellationToken cancellationToken)
    {
        var material = await dbContext.RawMaterials.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == rawMaterialId, cancellationToken)
            ?? throw new DomainValidationException("Raw material was not found.");

        var demands = await dbContext.MaterialRequirements.AsNoTracking()
            .Where(x => x.RawMaterialId == rawMaterialId && MaterialAvailabilityRules.ActiveDemandStatuses.Contains(x.ProductionPlan.Status))
            .Select(x => new { x.ProductionPlan.PlannedCompletionDate, x.RequiredQuantity })
            .ToArrayAsync(cancellationToken);
        var supplies = await dbContext.IncomingPurchaseOrderItems.AsNoTracking()
            .Where(x => x.RawMaterialId == rawMaterialId && MaterialAvailabilityRules.EligibleIncomingStatuses.Contains(x.IncomingPurchaseOrder.Status))
            .Select(x => new { x.IncomingPurchaseOrder.ExpectedDate, x.OrderedQuantity, x.ReceivedQuantity })
            .ToArrayAsync(cancellationToken);

        var onHandAvailable = MaterialAvailabilityRules.CalculateOnHandAvailable(material.CurrentStock, material.ReservedStock);
        var timeline = MaterialAvailabilityRules.BuildTimeline(
            onHandAvailable,
            demands.Select(x => new MaterialDemand(x.PlannedCompletionDate, x.RequiredQuantity)),
            supplies.Select(x => new IncomingSupply(x.ExpectedDate, MaterialAvailabilityRules.CalculateOutstandingQuantity(x.OrderedQuantity, x.ReceivedQuantity))),
            Today());

        return MaterialAvailabilityRules.Evaluate(onHandAvailable, timeline).ShortageQuantity;
    }

    private Task<bool> HasActiveRequestAsync(long productionPlanId, long rawMaterialId, CancellationToken cancellationToken) =>
        dbContext.PurchaseRequests.AsNoTracking().AnyAsync(
            x => x.SourceProductionPlanId == productionPlanId
                && PurchaseRequestRules.ActiveStatuses.Contains(x.Status)
                && x.Items.Any(item => item.RawMaterialId == rawMaterialId),
            cancellationToken);

    private async Task<string> NextRequestNumberAsync(CancellationToken cancellationToken)
    {
        var count = await dbContext.PurchaseRequests.CountAsync(cancellationToken);
        return $"PR-{count + 1:D6}";
    }

    private async Task<PurchaseRequestDto?> GetPurchaseRequestAsync(long id, CancellationToken cancellationToken)
    {
        var request = await dbContext.PurchaseRequests.AsNoTracking()
            .Include(x => x.SourceProductionPlan)
            .Include(x => x.IncomingPurchaseOrder)
            .Include(x => x.Items).ThenInclude(x => x.RawMaterial)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return request is null ? null : PurchaseRequestMapper.Map(request);
    }

    private async Task<ILookup<long, AffectedOrderDto>> LoadAffectedOrdersAsync(long[] materialIds, CancellationToken cancellationToken)
    {
        var rows = await dbContext.MaterialRequirements.AsNoTracking()
            .Where(x => materialIds.Contains(x.RawMaterialId) && MaterialAvailabilityRules.ActiveDemandStatuses.Contains(x.ProductionPlan.Status))
            .Select(x => new
            {
                x.RawMaterialId,
                x.ProductionPlan.CustomerOrderId,
                x.ProductionPlan.CustomerOrder.OrderNumber,
                x.ProductionPlan.CustomerOrder.CustomerName,
                ProductionPlanId = x.ProductionPlanId,
                x.ProductionPlan.PlanNumber,
                x.ProductionPlan.PlannedCompletionDate,
                x.RequiredQuantity
            })
            .ToArrayAsync(cancellationToken);

        return rows
            .OrderBy(x => x.PlannedCompletionDate).ThenBy(x => x.PlanNumber)
            .ToLookup(x => x.RawMaterialId, x => new AffectedOrderDto(
                x.CustomerOrderId, x.OrderNumber, x.CustomerName, x.ProductionPlanId, x.PlanNumber, x.PlannedCompletionDate, x.RequiredQuantity));
    }

    private async Task<ILookup<long, LatePurchaseOrderDto>> LoadLatePurchaseOrdersAsync(long[] materialIds, CancellationToken cancellationToken)
    {
        var today = Today();
        var rows = await dbContext.IncomingPurchaseOrderItems.AsNoTracking()
            .Where(x => materialIds.Contains(x.RawMaterialId))
            .Select(x => new
            {
                x.RawMaterialId,
                x.IncomingPurchaseOrderId,
                x.IncomingPurchaseOrder.PurchaseOrderNumber,
                x.IncomingPurchaseOrder.ExpectedDate,
                x.IncomingPurchaseOrder.Status,
                x.OrderedQuantity,
                x.ReceivedQuantity
            })
            .ToArrayAsync(cancellationToken);

        return rows
            .Select(x => new
            {
                x.RawMaterialId,
                x.IncomingPurchaseOrderId,
                x.PurchaseOrderNumber,
                x.ExpectedDate,
                x.Status,
                Outstanding = MaterialAvailabilityRules.CalculateOutstandingQuantity(x.OrderedQuantity, x.ReceivedQuantity)
            })
            .Where(x => PurchaseRequestRules.IsLate(x.Status, x.ExpectedDate, today, x.Outstanding))
            .OrderBy(x => x.ExpectedDate)
            .ToLookup(x => x.RawMaterialId, x => new LatePurchaseOrderDto(
                x.IncomingPurchaseOrderId,
                x.PurchaseOrderNumber,
                x.ExpectedDate,
                PurchaseRequestRules.CalculateDelayDays(x.ExpectedDate, today),
                x.Outstanding));
    }

    private static MaterialShortageDto Map(
        MaterialAvailabilityDto availability,
        ILookup<long, AffectedOrderDto> affectedOrders,
        ILookup<long, LatePurchaseOrderDto> latePurchaseOrders) =>
        new(
            availability.RawMaterialId,
            availability.RawMaterialCode,
            availability.RawMaterialName,
            availability.Unit,
            availability.RequiredQuantity,
            availability.OnHandAvailable,
            availability.EligibleIncoming,
            availability.ProjectedAvailable,
            availability.ShortageQuantity,
            availability.MaterialRequiredDate,
            availability.EvaluationDate,
            [.. affectedOrders[availability.RawMaterialId]],
            [.. latePurchaseOrders[availability.RawMaterialId]]);

    private static void Validate(CreatePurchaseRequestCommand command)
    {
        if (command.RawMaterialId <= 0) throw new DomainValidationException("Raw material is required.");
        if (command.SourceProductionPlanId <= 0) throw new DomainValidationException("Source production plan is required.");
        if (command.RequestedQuantity <= 0) throw new DomainValidationException("Requested quantity must be greater than zero.");
        if (command.ExpectedDate == default) throw new DomainValidationException("Expected date is required.");
    }

    private static void EnsureCanCreate(ClaimsPrincipal actor)
    {
        if (!actor.IsInRole(RoleNames.Admin) && !actor.IsInRole(RoleNames.Manager) && !actor.IsInRole(RoleNames.Planner))
            throw new UnauthorizedAccessException("Admin, Manager, or Planner role is required to create a purchase request.");
    }

    private static long CurrentUserId(ClaimsPrincipal actor)
    {
        var value = actor.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(value, out var id) ? id : throw new DomainValidationException("An authenticated user is required to create a purchase request.");
    }

    private static DateTime NormalizeDate(DateTime value) => DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
    private DateTime Today() => timeProvider.GetUtcNow().UtcDateTime.Date;
}
