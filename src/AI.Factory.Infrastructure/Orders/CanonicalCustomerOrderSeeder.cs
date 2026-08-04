using AI.Factory.Core.Domain;
using AI.Factory.Infrastructure.Identity;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Factory.Infrastructure.Orders;

public static class CanonicalCustomerOrderSeeder
{
    private static readonly (string Number, string Formulation, decimal Quantity, int DeliveryOffset, string Priority, CustomerOrderStatus Status)[] Orders =
    [
        ("SO-DEMO-001", "FM-DEMO-001", 10_000, 3, "Urgent", CustomerOrderStatus.Planned),
        ("SO-0002", "FM-002", 5_000, 10, "High", CustomerOrderStatus.Planned),
        ("SO-0003", "FM-003", 2_250, 7, "Normal", CustomerOrderStatus.InProduction),
        ("SO-0004", "FM-004", 1_200, -2, "Normal", CustomerOrderStatus.Completed),
        ("SO-0005", "FM-005", 800, 14, "Normal", CustomerOrderStatus.Draft),
        ("SO-0006", "FM-002", 2_000, 12, "Normal", CustomerOrderStatus.Planned),
        ("SO-0007", "FM-003", 1_500, -1, "Normal", CustomerOrderStatus.Completed),
        ("SO-0008", "FM-004", 1_800, 9, "High", CustomerOrderStatus.Planned),
        ("SO-0009", "FM-005", 1_600, 11, "Normal", CustomerOrderStatus.Planned),
        ("SO-0010", "FM-003", 750, 15, "Normal", CustomerOrderStatus.Draft)
    ];

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var anchor = await db.CustomerOrders.AsNoTracking().SingleOrDefaultAsync(x => x.OrderNumber == "SO-DEMO-001", cancellationToken);
        var seedDate = anchor?.CreatedAt.Date ?? services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime.Date;
        seedDate = DateTime.SpecifyKind(seedDate, DateTimeKind.Utc);

        var planner = await services.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync("planner.demo")
            ?? throw new InvalidOperationException("Seed identity before customer orders.");
        var formulationIds = await db.Formulations.Where(x => x.IsActive).ToDictionaryAsync(x => x.Code, x => x.Id, cancellationToken);
        var existing = await db.CustomerOrders.Select(x => x.OrderNumber).ToHashSetAsync(cancellationToken);

        foreach (var item in Orders.Where(x => !existing.Contains(x.Number)))
        {
            if (!formulationIds.TryGetValue(item.Formulation, out var formulationId))
                throw new InvalidOperationException($"Canonical formulation {item.Formulation} is missing.");

            db.CustomerOrders.Add(new CustomerOrder
            {
                OrderNumber = item.Number,
                CustomerName = $"Demo Customer {item.Number[3..]}",
                FormulationId = formulationId,
                Quantity = item.Quantity,
                DeliveryDate = seedDate.AddDays(item.DeliveryOffset),
                Priority = item.Priority,
                Status = item.Status,
                CreatedByUserId = planner.Id,
                CreatedAt = seedDate,
                UpdatedAt = seedDate
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
