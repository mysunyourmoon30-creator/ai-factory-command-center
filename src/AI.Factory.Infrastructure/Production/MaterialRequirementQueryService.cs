using AI.Factory.Core.Domain;
using AI.Factory.Core.Production;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.Factory.Infrastructure.Production;

public sealed class MaterialRequirementQueryService(AppDbContext dbContext, TimeProvider timeProvider)
    : IMaterialRequirementQueryService
{
    public async Task<IReadOnlyCollection<MaterialAvailabilityDto>> ListAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var materials = await dbContext.RawMaterials.AsNoTracking().OrderBy(x => x.Code).ToArrayAsync(cancellationToken);
        var demands = await LoadDemandsAsync(cancellationToken);
        var supplies = await LoadSuppliesAsync(cancellationToken);
        var today = Today();

        return materials
            .Select(material => BuildAvailability(material, Demands(demands, material.Id), Supplies(supplies, material.Id), today))
            .ToArray();
    }

    public async Task<PlanRequirementDto?> GetByProductionPlanAsync(long productionPlanId, CancellationToken cancellationToken = default)
    {
        var plan = await dbContext.ProductionPlans.AsNoTracking()
            .Include(x => x.MaterialRequirements).ThenInclude(x => x.RawMaterial)
            .SingleOrDefaultAsync(x => x.Id == productionPlanId, cancellationToken);
        if (plan is null) return null;

        var demands = await LoadDemandsAsync(cancellationToken);
        var supplies = await LoadSuppliesAsync(cancellationToken);
        var today = Today();
        var requiredDate = plan.PlannedCompletionDate.Date;

        var lines = plan.MaterialRequirements
            .OrderBy(x => x.RawMaterial.Code)
            .Select(requirement =>
            {
                var material = requirement.RawMaterial;
                var onHandAvailable = MaterialAvailabilityRules.CalculateOnHandAvailable(material.CurrentStock, material.ReservedStock);
                var cumulativeIncoming = MaterialAvailabilityRules.CalculateCumulativeIncoming(Supplies(supplies, material.Id), requiredDate, today);
                return new PlanRequirementLineDto(
                    material.Id,
                    material.Code,
                    material.Name,
                    material.Unit,
                    requirement.RequiredQuantity,
                    MaterialAvailabilityRules.CalculateCumulativeRequired(Demands(demands, material.Id), requiredDate),
                    material.CurrentStock,
                    material.ReservedStock,
                    onHandAvailable,
                    cumulativeIncoming,
                    MaterialAvailabilityRules.CalculateAvailableByDate(onHandAvailable, cumulativeIncoming));
            })
            .ToArray();

        return new PlanRequirementDto(plan.Id, plan.PlanNumber, requiredDate, plan.Status, plan.RequiredBatch, lines);
    }

    /// <summary>
    /// Day 6 evaluates the whole active horizon, so the headline row is reported at the last active
    /// Required Date. A material with no active demand keeps its on-hand position unchanged.
    /// </summary>
    private static MaterialAvailabilityDto BuildAvailability(
        RawMaterial material,
        IReadOnlyCollection<MaterialDemand> demands,
        IReadOnlyCollection<IncomingSupply> supplies,
        DateTime today)
    {
        var onHandAvailable = MaterialAvailabilityRules.CalculateOnHandAvailable(material.CurrentStock, material.ReservedStock);
        var timeline = MaterialAvailabilityRules.BuildTimeline(onHandAvailable, demands, supplies, today);
        var evaluation = timeline.Count == 0 ? null : timeline[^1];

        return new MaterialAvailabilityDto(
            material.Id,
            material.Code,
            material.Name,
            material.Unit,
            material.CurrentStock,
            material.ReservedStock,
            onHandAvailable,
            evaluation?.RequiredDate,
            evaluation?.CumulativeRequired ?? 0m,
            evaluation?.CumulativeIncoming ?? 0m,
            evaluation?.AvailableByDate ?? onHandAvailable,
            timeline);
    }

    private async Task<ILookup<long, MaterialDemand>> LoadDemandsAsync(CancellationToken cancellationToken)
    {
        var rows = await dbContext.MaterialRequirements.AsNoTracking()
            .Where(x => MaterialAvailabilityRules.ActiveDemandStatuses.Contains(x.ProductionPlan.Status))
            .Select(x => new { x.RawMaterialId, x.ProductionPlan.PlannedCompletionDate, x.RequiredQuantity })
            .ToArrayAsync(cancellationToken);

        return rows.ToLookup(x => x.RawMaterialId, x => new MaterialDemand(x.PlannedCompletionDate, x.RequiredQuantity));
    }

    private async Task<ILookup<long, IncomingSupply>> LoadSuppliesAsync(CancellationToken cancellationToken)
    {
        var rows = await dbContext.IncomingPurchaseOrderItems.AsNoTracking()
            .Where(x => MaterialAvailabilityRules.EligibleIncomingStatuses.Contains(x.IncomingPurchaseOrder.Status))
            .Select(x => new { x.RawMaterialId, x.IncomingPurchaseOrder.ExpectedDate, x.OrderedQuantity, x.ReceivedQuantity })
            .ToArrayAsync(cancellationToken);

        return rows.ToLookup(
            x => x.RawMaterialId,
            x => new IncomingSupply(x.ExpectedDate, MaterialAvailabilityRules.CalculateOutstandingQuantity(x.OrderedQuantity, x.ReceivedQuantity)));
    }

    private static MaterialDemand[] Demands(ILookup<long, MaterialDemand> demands, long rawMaterialId) => [.. demands[rawMaterialId]];
    private static IncomingSupply[] Supplies(ILookup<long, IncomingSupply> supplies, long rawMaterialId) => [.. supplies[rawMaterialId]];
    private DateTime Today() => timeProvider.GetUtcNow().UtcDateTime.Date;
}
