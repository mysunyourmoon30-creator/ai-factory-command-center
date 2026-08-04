using AI.Factory.Core.Domain;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Factory.Infrastructure.MasterData;

public static class CanonicalMasterDataSeeder
{
    private static readonly (string Code, string Name, decimal Current, decimal Reserved)[] RawMaterials =
    [
        ("RM-001", "Polymer Base A", 4200, 450), ("RM-002", "Additive B", 5000, 300),
        ("RM-003", "Pigment C", 3500, 100), ("RM-004", "Filler D", 10000, 500),
        ("RM-005", "Binder E", 8000, 400), ("RM-006", "Catalyst F", 5000, 200),
        ("RM-007", "Stabilizer G", 6000, 300), ("RM-008", "Solvent H", 9000, 500),
        ("RM-009", "Resin I", 7000, 300), ("RM-010", "Modifier J", 4000, 100)
    ];

    private static readonly (string Code, string Name, decimal Batch, (string Code, decimal Weight)[] Materials)[] Formulations =
    [
        ("FM-DEMO-001", "Demo Formulation 001", 500, [("RM-001", 250), ("RM-002", 150), ("RM-003", 100)]),
        ("FM-002", "Formulation 002", 1000, [("RM-004", 400), ("RM-005", 350), ("RM-006", 250)]),
        ("FM-003", "Formulation 003", 750, [("RM-002", 250), ("RM-007", 250), ("RM-008", 250)]),
        ("FM-004", "Formulation 004", 600, [("RM-003", 200), ("RM-009", 250), ("RM-010", 150)]),
        ("FM-005", "Formulation 005", 400, [("RM-005", 150), ("RM-008", 100), ("RM-009", 100), ("RM-010", 50)])
    ];

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var existingCodes = await db.RawMaterials.Select(x => x.Code).ToHashSetAsync(cancellationToken);
        foreach (var item in RawMaterials.Where(x => !existingCodes.Contains(x.Code)))
            db.RawMaterials.Add(new RawMaterial { Code = item.Code, Name = item.Name, Unit = "kg", CurrentStock = item.Current, ReservedStock = item.Reserved, LeadTimeDays = 7, IsActive = true, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync(cancellationToken);

        var materialIds = await db.RawMaterials.ToDictionaryAsync(x => x.Code, x => x.Id, cancellationToken);
        var existingFormulations = await db.Formulations.Select(x => x.Code).ToHashSetAsync(cancellationToken);
        foreach (var item in Formulations.Where(x => !existingFormulations.Contains(x.Code)))
        {
            db.Formulations.Add(new Formulation
            {
                Code = item.Code,
                Name = item.Name,
                BatchSize = item.Batch,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
                Materials = item.Materials.Select(x => new FormulationMaterial { RawMaterialId = materialIds[x.Code], WeightPerBatch = x.Weight }).ToList()
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
