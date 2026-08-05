using AI.Factory.Core.Domain;

namespace AI.Factory.Core.Production;

public sealed record MaterialDemand(DateTime RequiredDate, decimal Quantity);
public sealed record IncomingSupply(DateTime ExpectedDate, decimal OutstandingQuantity);
public sealed record MaterialAvailabilityPoint(DateTime RequiredDate, decimal CumulativeRequired, decimal CumulativeIncoming, decimal AvailableByDate);

public sealed record MaterialAvailabilityDto(
    long RawMaterialId,
    string RawMaterialCode,
    string RawMaterialName,
    string Unit,
    decimal CurrentStock,
    decimal ReservedStock,
    decimal OnHandAvailable,
    DateTime? EvaluationDate,
    DateTime? MaterialRequiredDate,
    decimal RequiredQuantity,
    decimal EligibleIncoming,
    decimal ProjectedAvailable,
    decimal ShortageQuantity,
    IReadOnlyCollection<MaterialAvailabilityPoint> Timeline);

/// <summary>
/// The single point on the timeline that every displayed figure is read from, so a shortage row
/// reconciles with itself. <see cref="MaterialRequiredDate"/> is null when nothing is short.
/// </summary>
public sealed record MaterialShortageEvaluation(
    DateTime? EvaluationDate,
    DateTime? MaterialRequiredDate,
    decimal ShortageQuantity,
    decimal CumulativeRequired,
    decimal CumulativeIncoming,
    decimal AvailableByDate);

public sealed record PlanRequirementLineDto(
    long RawMaterialId,
    string RawMaterialCode,
    string RawMaterialName,
    string Unit,
    decimal PlanRequiredQuantity,
    decimal CumulativeRequired,
    decimal CurrentStock,
    decimal ReservedStock,
    decimal OnHandAvailable,
    decimal EligibleIncoming,
    decimal ProjectedAvailable);

public sealed record PlanRequirementDto(
    long ProductionPlanId,
    string PlanNumber,
    DateTime RequiredDate,
    ProductionPlanStatus PlanStatus,
    int RequiredBatch,
    IReadOnlyCollection<PlanRequirementLineDto> Lines);

public interface IMaterialRequirementQueryService
{
    Task<IReadOnlyCollection<MaterialAvailabilityDto>> ListAvailabilityAsync(CancellationToken cancellationToken = default);
    Task<PlanRequirementDto?> GetByProductionPlanAsync(long productionPlanId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Locked cumulative demand, availability, and shortage rules
/// (Master Scope V4, Module 6.1-6.5 and 15.3-15.6).
/// </summary>
public static class MaterialAvailabilityRules
{
    /// <summary>Production plan statuses whose requirements count as active demand.</summary>
    public static readonly ProductionPlanStatus[] ActiveDemandStatuses =
        [ProductionPlanStatus.Planned, ProductionPlanStatus.InProduction];

    /// <summary>Purchase order statuses that can still deliver outstanding supply.</summary>
    public static readonly IncomingPurchaseOrderStatus[] EligibleIncomingStatuses =
        [IncomingPurchaseOrderStatus.Open, IncomingPurchaseOrderStatus.Partial];

    public static bool IsActiveDemand(ProductionPlanStatus status) => ActiveDemandStatuses.Contains(status);

    /// <summary>
    /// On-hand Available = Current Stock - Reserved Stock. The result may be negative and the
    /// negative value must flow into every downstream calculation; only the UI floors it at zero.
    /// </summary>
    public static decimal CalculateOnHandAvailable(decimal currentStock, decimal reservedStock) =>
        currentStock - reservedStock;

    public static decimal CalculateOutstandingQuantity(decimal orderedQuantity, decimal receivedQuantity) =>
        orderedQuantity - receivedQuantity;

    /// <summary>Sum of active requirements whose Required Date falls on or before <paramref name="date"/>.</summary>
    public static decimal CalculateCumulativeRequired(IEnumerable<MaterialDemand> demands, DateTime date) =>
        demands.Where(x => x.RequiredDate.Date <= date.Date).Sum(x => x.Quantity);

    /// <summary>
    /// Sum of outstanding supply expected on or before <paramref name="date"/>. Supply already past
    /// its expected date is late and is never counted, so it cannot mask a shortage.
    /// </summary>
    public static decimal CalculateCumulativeIncoming(IEnumerable<IncomingSupply> supplies, DateTime date, DateTime today) =>
        supplies
            .Where(x => x.OutstandingQuantity > 0 && x.ExpectedDate.Date >= today.Date && x.ExpectedDate.Date <= date.Date)
            .Sum(x => x.OutstandingQuantity);

    public static decimal CalculateAvailableByDate(decimal onHandAvailable, decimal cumulativeIncoming) =>
        onHandAvailable + cumulativeIncoming;

    /// <summary>
    /// Evaluates every distinct active Required Date in ascending order. Each date is evaluated on its
    /// own, so supply arriving for a later plan is never cut off by an earlier plan's date.
    /// </summary>
    public static IReadOnlyList<MaterialAvailabilityPoint> BuildTimeline(
        decimal onHandAvailable,
        IEnumerable<MaterialDemand> demands,
        IEnumerable<IncomingSupply> supplies,
        DateTime today)
    {
        var demandList = demands as IReadOnlyCollection<MaterialDemand> ?? demands.ToArray();
        var supplyList = supplies as IReadOnlyCollection<IncomingSupply> ?? supplies.ToArray();

        return demandList
            .Select(x => x.RequiredDate.Date)
            .Distinct()
            .OrderBy(x => x)
            .Select(date =>
            {
                var cumulativeIncoming = CalculateCumulativeIncoming(supplyList, date, today);
                return new MaterialAvailabilityPoint(
                    date,
                    CalculateCumulativeRequired(demandList, date),
                    cumulativeIncoming,
                    CalculateAvailableByDate(onHandAvailable, cumulativeIncoming));
            })
            .ToArray();
    }

    public static decimal CalculateDeficit(decimal cumulativeRequired, decimal availableByDate) =>
        Math.Max(cumulativeRequired - availableByDate, 0m);

    /// <summary>
    /// Shortage Quantity is the largest Deficit across the timeline. Material Required Date is the
    /// first date that runs short, and Evaluation Date is the first date reaching that largest
    /// deficit. When nothing is short the row is still reported, evaluated at the last active
    /// Required Date, so the on-hand position stays visible.
    /// </summary>
    public static MaterialShortageEvaluation Evaluate(decimal onHandAvailable, IReadOnlyList<MaterialAvailabilityPoint> timeline)
    {
        if (timeline.Count == 0)
        {
            return new MaterialShortageEvaluation(null, null, 0m, 0m, 0m, onHandAvailable);
        }

        var deficits = timeline.Select(x => CalculateDeficit(x.CumulativeRequired, x.AvailableByDate)).ToArray();
        var shortageQuantity = deficits.Max();
        var shortfallIndex = Array.FindIndex(deficits, x => x > 0);
        var evaluationIndex = shortageQuantity > 0 ? Array.FindIndex(deficits, x => x == shortageQuantity) : timeline.Count - 1;
        var evaluation = timeline[evaluationIndex];

        return new MaterialShortageEvaluation(
            evaluation.RequiredDate,
            shortfallIndex < 0 ? null : timeline[shortfallIndex].RequiredDate,
            shortageQuantity,
            evaluation.CumulativeRequired,
            evaluation.CumulativeIncoming,
            evaluation.AvailableByDate);
    }
}
