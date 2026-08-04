using System.Security.Claims;
using AI.Factory.Core.Domain;
using AI.Factory.Core.MasterData;
using AI.Factory.Core.Orders;
using AI.Factory.Core.Production;
using AI.Factory.Core.Security;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.Factory.Infrastructure.Production;

public sealed class ProductionPlanService(
    AppDbContext dbContext,
    IAuditWriter auditWriter,
    IOrderRiskCalculator riskCalculator,
    TimeProvider timeProvider) : IProductionPlanService
{
    public async Task<IReadOnlyCollection<ProductionPlanDto>> ListAsync(CancellationToken cancellationToken = default) =>
        (await Query().OrderBy(x => x.PlannedCompletionDate).ThenBy(x => x.PlanNumber).ToArrayAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<ProductionPlanDto?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await Query().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyCollection<MachineOptionDto>> ListMachinesAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Machines.AsNoTracking().OrderBy(x => x.MachineCode)
            .Select(x => new MachineOptionDto(x.Id, x.MachineCode, x.MachineName)).ToArrayAsync(cancellationToken);

    public async Task<ProductionPlanDto> CreateAsync(CreateProductionPlanCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        EnsureCanManage(actor);
        Validate(command);
        var planNumber = command.PlanNumber.Trim();
        if (await dbContext.ProductionPlans.AnyAsync(x => x.PlanNumber == planNumber, cancellationToken))
            throw new BusinessConflictException("Production plan number already exists.");
        if (!await dbContext.Machines.AnyAsync(x => x.Id == command.MachineId, cancellationToken))
            throw new DomainValidationException("A valid machine is required.");

        var order = await dbContext.CustomerOrders
            .Include(x => x.ProductionPlan)
            .Include(x => x.Formulation).ThenInclude(x => x.Materials).ThenInclude(x => x.RawMaterial)
            .SingleOrDefaultAsync(x => x.Id == command.CustomerOrderId, cancellationToken)
            ?? throw new DomainValidationException("Customer order was not found.");
        if (order.ProductionPlan is not null)
            throw new BusinessConflictException("This customer order already has a production plan.");
        if (order.Status != CustomerOrderStatus.Planned)
            throw new DomainValidationException("Customer order must be Planned before creating a production plan.");
        if (order.Formulation.BatchSize <= 0 || order.Formulation.Materials.Count == 0)
            throw new DomainValidationException("The order formulation must have a positive batch size and materials.");

        int requiredBatch;
        try { requiredBatch = ProductionPlanRules.CalculateRequiredBatch(order.Quantity, order.Formulation.BatchSize); }
        catch (OverflowException) { throw new DomainValidationException("Required batch exceeds the supported range."); }
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var plan = new ProductionPlan
        {
            PlanNumber = planNumber,
            CustomerOrderId = order.Id,
            MachineId = command.MachineId,
            RequiredBatch = requiredBatch,
            PlannedCompletionDate = NormalizeDate(command.PlannedCompletionDate),
            Status = ProductionPlanStatus.Planned,
            CreatedByUserId = CurrentUserId(actor),
            CreatedAt = now,
            MaterialRequirements = order.Formulation.Materials.Select(material => new MaterialRequirement
            {
                RawMaterialId = material.RawMaterialId,
                RequiredQuantity = ProductionPlanRules.CalculateRequiredMaterial(requiredBatch, material.WeightPerBatch),
                CalculatedAt = now
            }).ToArray()
        };

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            dbContext.ProductionPlans.Add(plan);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            throw new BusinessConflictException("A production plan already exists for this order or plan number.");
        }

        await auditWriter.WriteAsync("Create Production Plan", nameof(ProductionPlan), plan.Id, "Success", cancellationToken: cancellationToken);
        return (await GetAsync(plan.Id, cancellationToken))!;
    }

    public async Task<ProductionPlanDto?> TransitionAsync(long id, TransitionProductionPlanCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        EnsureCanManage(actor);
        var plan = await dbContext.ProductionPlans.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (plan is null) return null;
        EnsureRowVersion(plan, command.RowVersion);
        var expected = plan.Status switch
        {
            ProductionPlanStatus.Planned => ProductionPlanStatus.InProduction,
            ProductionPlanStatus.InProduction => ProductionPlanStatus.Completed,
            _ => (ProductionPlanStatus?)null
        };
        if (expected != command.TargetStatus)
            throw new DomainValidationException($"Invalid production-plan transition from {plan.Status} to {command.TargetStatus}.");

        dbContext.Entry(plan).Property(x => x.RowVersion).OriginalValue = command.RowVersion;
        plan.Status = command.TargetStatus;
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException("Production plan changed; reload before saving."); }
        await auditWriter.WriteAsync("Transition Production Plan", nameof(ProductionPlan), plan.Id, $"Success: {plan.Status}", cancellationToken: cancellationToken);
        return await GetAsync(plan.Id, cancellationToken);
    }

    private IQueryable<ProductionPlan> Query() => dbContext.ProductionPlans.AsNoTracking()
        .Include(x => x.CustomerOrder).ThenInclude(x => x.Formulation)
        .Include(x => x.Machine)
        .Include(x => x.MaterialRequirements).ThenInclude(x => x.RawMaterial);

    private ProductionPlanDto Map(ProductionPlan x) => new(
        x.Id, x.PlanNumber, x.CustomerOrderId, x.CustomerOrder.OrderNumber, x.CustomerOrder.Formulation.Code,
        x.CustomerOrder.Quantity, x.MachineId, x.Machine.MachineCode, x.Machine.MachineName, x.RequiredBatch,
        x.PlannedCompletionDate, x.Status, riskCalculator.Calculate(x.CustomerOrder.DeliveryDate, x.PlannedCompletionDate),
        x.MaterialRequirements.OrderBy(m => m.RawMaterial.Code).Select(m => new ProductionPlanMaterialDto(
            m.RawMaterialId, m.RawMaterial.Code, m.RawMaterial.Name, m.RawMaterial.Unit,
            m.RequiredQuantity, m.RawMaterial.CurrentStock, m.RawMaterial.ReservedStock)).ToArray(), x.RowVersion);

    private static void Validate(CreateProductionPlanCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.PlanNumber) || command.PlanNumber.Trim().Length > 30)
            throw new DomainValidationException("Plan number is required and limited to 30 characters.");
        if (command.CustomerOrderId <= 0) throw new DomainValidationException("Customer order is required.");
        if (command.MachineId <= 0) throw new DomainValidationException("Machine is required.");
        if (command.PlannedCompletionDate == default) throw new DomainValidationException("Planned completion date is required.");
    }

    private static void EnsureRowVersion(ProductionPlan plan, byte[] rowVersion)
    {
        if (rowVersion is null || !plan.RowVersion.SequenceEqual(rowVersion))
            throw new ConcurrencyConflictException("Production plan changed; reload before saving.");
    }

    private static void EnsureCanManage(ClaimsPrincipal actor)
    {
        if (!actor.IsInRole(RoleNames.Admin) && !actor.IsInRole(RoleNames.Planner))
            throw new UnauthorizedAccessException("Admin or Planner role is required to manage production plans.");
    }

    private static long CurrentUserId(ClaimsPrincipal actor)
    {
        var value = actor.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(value, out var id) ? id : throw new DomainValidationException("An authenticated user is required to create a production plan.");
    }

    private static DateTime NormalizeDate(DateTime value) => DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
}
