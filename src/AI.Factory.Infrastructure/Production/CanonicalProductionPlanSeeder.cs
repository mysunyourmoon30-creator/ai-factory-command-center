using AI.Factory.Core.Domain;
using AI.Factory.Core.Production;
using AI.Factory.Infrastructure.Identity;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Factory.Infrastructure.Production;

public static class CanonicalProductionPlanSeeder
{
    private static readonly (string Code, string Name, MachineRunningStatus Running, decimal Temperature, decimal Speed, RiskStatus Alert)[] Machines =
    [
        ("Machine-01", "Machine 01", MachineRunningStatus.Running, 72, 80, RiskStatus.Normal),
        ("Machine-02", "Machine 02", MachineRunningStatus.Running, 95, 60, RiskStatus.Critical),
        ("Machine-03", "Machine 03", MachineRunningStatus.Stopped, 35, 0, RiskStatus.Warning)
    ];

    private static readonly (string Plan, string Order, string Machine, int CompletionOffset, ProductionPlanStatus Status)[] Plans =
    [
        ("PP-DEMO-001", "SO-DEMO-001", "Machine-02", 5, ProductionPlanStatus.Planned),
        ("PP-0002", "SO-0002", "Machine-01", 4, ProductionPlanStatus.Planned),
        ("PP-0003", "SO-0003", "Machine-01", 2, ProductionPlanStatus.InProduction),
        ("PP-0004", "SO-0004", "Machine-03", -5, ProductionPlanStatus.Completed),
        ("PP-0006", "SO-0006", "Machine-01", 5, ProductionPlanStatus.Planned),
        ("PP-0007", "SO-0007", "Machine-01", -3, ProductionPlanStatus.Completed),
        ("PP-0008", "SO-0008", "Machine-03", 2, ProductionPlanStatus.Planned),
        ("PP-0009", "SO-0009", "Machine-01", 3, ProductionPlanStatus.Planned)
    ];

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var anchor = await db.CustomerOrders.AsNoTracking().SingleOrDefaultAsync(x => x.OrderNumber == "SO-DEMO-001", cancellationToken)
            ?? throw new InvalidOperationException("Seed canonical customer orders before production plans.");
        var seedDate = DateTime.SpecifyKind(anchor.CreatedAt.Date, DateTimeKind.Utc);
        var planner = await services.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync("planner.demo")
            ?? throw new InvalidOperationException("Seed identity before production plans.");

        var existingMachineCodes = await db.Machines.Select(x => x.MachineCode).ToHashSetAsync(cancellationToken);
        foreach (var item in Machines.Where(x => !existingMachineCodes.Contains(x.Code)))
        {
            db.Machines.Add(new Machine
            {
                MachineCode = item.Code,
                MachineName = item.Name,
                RunningStatus = item.Running,
                Temperature = item.Temperature,
                Speed = item.Speed,
                AlertStatus = item.Alert,
                LastUpdated = seedDate
            });
        }
        await db.SaveChangesAsync(cancellationToken);

        var machineIds = await db.Machines.ToDictionaryAsync(x => x.MachineCode, x => x.Id, cancellationToken);
        var orders = await db.CustomerOrders
            .Include(x => x.ProductionPlan)
            .Include(x => x.Formulation).ThenInclude(x => x.Materials)
            .Where(x => Plans.Select(p => p.Order).Contains(x.OrderNumber))
            .ToDictionaryAsync(x => x.OrderNumber, cancellationToken);
        var existingPlanNumbers = await db.ProductionPlans.Select(x => x.PlanNumber).ToHashSetAsync(cancellationToken);

        foreach (var item in Plans.Where(x => !existingPlanNumbers.Contains(x.Plan)))
        {
            if (!orders.TryGetValue(item.Order, out var order))
                throw new InvalidOperationException($"Canonical order {item.Order} is missing.");
            if (order.ProductionPlan is not null) continue;
            var requiredBatch = ProductionPlanRules.CalculateRequiredBatch(order.Quantity, order.Formulation.BatchSize);
            db.ProductionPlans.Add(new ProductionPlan
            {
                PlanNumber = item.Plan,
                CustomerOrderId = order.Id,
                MachineId = machineIds[item.Machine],
                RequiredBatch = requiredBatch,
                PlannedCompletionDate = seedDate.AddDays(item.CompletionOffset),
                Status = item.Status,
                CreatedByUserId = planner.Id,
                CreatedAt = seedDate,
                MaterialRequirements = order.Formulation.Materials.Select(material => new MaterialRequirement
                {
                    RawMaterialId = material.RawMaterialId,
                    RequiredQuantity = ProductionPlanRules.CalculateRequiredMaterial(requiredBatch, material.WeightPerBatch),
                    CalculatedAt = seedDate
                }).ToArray()
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
