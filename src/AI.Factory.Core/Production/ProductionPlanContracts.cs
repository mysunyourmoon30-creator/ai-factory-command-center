using System.Security.Claims;
using AI.Factory.Core.Domain;

namespace AI.Factory.Core.Production;

public sealed record ProductionPlanMaterialDto(
    long RawMaterialId,
    string RawMaterialCode,
    string RawMaterialName,
    string Unit,
    decimal RequiredQuantity,
    decimal CurrentStock,
    decimal ReservedStock);

public sealed record ProductionPlanDto(
    long Id,
    string PlanNumber,
    long CustomerOrderId,
    string OrderNumber,
    string FormulationCode,
    decimal OrderQuantity,
    long MachineId,
    string MachineCode,
    string MachineName,
    int RequiredBatch,
    DateTime PlannedCompletionDate,
    ProductionPlanStatus LifecycleStatus,
    RiskStatus RiskStatus,
    IReadOnlyCollection<ProductionPlanMaterialDto> MaterialRequirements,
    byte[] RowVersion);

public sealed record MachineOptionDto(long Id, string Code, string Name);
public sealed record CreateProductionPlanCommand(string PlanNumber, long CustomerOrderId, long MachineId, DateTime PlannedCompletionDate);
public sealed record TransitionProductionPlanCommand(ProductionPlanStatus TargetStatus, byte[] RowVersion);

public interface IProductionPlanService
{
    Task<IReadOnlyCollection<ProductionPlanDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<ProductionPlanDto?> GetAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<MachineOptionDto>> ListMachinesAsync(CancellationToken cancellationToken = default);
    Task<ProductionPlanDto> CreateAsync(CreateProductionPlanCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    Task<ProductionPlanDto?> TransitionAsync(long id, TransitionProductionPlanCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
}

public static class ProductionPlanRules
{
    public static int CalculateRequiredBatch(decimal orderQuantity, decimal batchSize)
    {
        if (orderQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(orderQuantity));
        if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
        return checked((int)Math.Ceiling(orderQuantity / batchSize));
    }

    public static decimal CalculateRequiredMaterial(int requiredBatch, decimal weightPerBatch)
    {
        if (requiredBatch <= 0) throw new ArgumentOutOfRangeException(nameof(requiredBatch));
        if (weightPerBatch <= 0) throw new ArgumentOutOfRangeException(nameof(weightPerBatch));
        return requiredBatch * weightPerBatch;
    }
}

public sealed class BusinessConflictException(string message) : Exception(message);
