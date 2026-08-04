using AI.Factory.Core.Domain;
using AI.Factory.Core.MasterData;
using AI.Factory.Core.Security;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.Factory.Infrastructure.MasterData;

public sealed class MasterDataService(AppDbContext dbContext, IAuditWriter auditWriter, TimeProvider timeProvider) : IMasterDataService
{
    public async Task<IReadOnlyCollection<RawMaterialDto>> ListRawMaterialsAsync(CancellationToken cancellationToken = default) =>
        (await dbContext.RawMaterials.AsNoTracking().OrderBy(x => x.Code).ToArrayAsync(cancellationToken)).Select(MapRaw).ToArray();

    public async Task<RawMaterialDto?> GetRawMaterialAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.RawMaterials.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity is null ? null : MapRaw(entity);
    }

    public async Task<RawMaterialDto> CreateRawMaterialAsync(RawMaterialCommand command, CancellationToken cancellationToken = default)
    {
        ValidateRaw(command);
        if (await dbContext.RawMaterials.AnyAsync(x => x.Code == command.Code.Trim(), cancellationToken))
            throw new DomainValidationException("Material code already exists.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var entity = new RawMaterial { Code = command.Code.Trim(), Name = command.Name.Trim(), Unit = command.Unit.Trim(), CurrentStock = command.CurrentStock, ReservedStock = command.ReservedStock, LeadTimeDays = command.LeadTimeDays, IsActive = command.IsActive, CreatedAt = now, UpdatedAt = now };
        dbContext.RawMaterials.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync("Create Raw Material", "RawMaterial", entity.Id, "Success", cancellationToken: cancellationToken);
        return MapRaw(entity);
    }

    public async Task<RawMaterialDto?> UpdateRawMaterialAsync(long id, RawMaterialCommand command, CancellationToken cancellationToken = default)
    {
        ValidateRaw(command);
        var entity = await dbContext.RawMaterials.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null) return null;
        if (command.RowVersion is null || !entity.RowVersion.SequenceEqual(command.RowVersion)) throw new ConcurrencyConflictException("Raw material changed; reload before saving.");
        if (await dbContext.RawMaterials.AnyAsync(x => x.Id != id && x.Code == command.Code.Trim(), cancellationToken)) throw new DomainValidationException("Material code already exists.");
        dbContext.Entry(entity).Property(x => x.RowVersion).OriginalValue = command.RowVersion;
        entity.Code = command.Code.Trim(); entity.Name = command.Name.Trim(); entity.Unit = command.Unit.Trim(); entity.CurrentStock = command.CurrentStock; entity.ReservedStock = command.ReservedStock; entity.LeadTimeDays = command.LeadTimeDays; entity.IsActive = command.IsActive; entity.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        try { await dbContext.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException("Raw material changed; reload before saving."); }
        await auditWriter.WriteAsync("Update Raw Material", "RawMaterial", entity.Id, "Success", cancellationToken: cancellationToken);
        return MapRaw(entity);
    }

    public async Task<IReadOnlyCollection<FormulationDto>> ListFormulationsAsync(CancellationToken cancellationToken = default) =>
        (await FormulationQuery().OrderBy(x => x.Code).ToArrayAsync(cancellationToken)).Select(MapFormulation).ToArray();

    public async Task<FormulationDto?> GetFormulationAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await FormulationQuery().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity is null ? null : MapFormulation(entity);
    }

    public Task<FormulationDto> CreateFormulationAsync(FormulationCommand command, CancellationToken cancellationToken = default) => SaveFormulationAsync(null, command, cancellationToken)!;
    public Task<FormulationDto?> UpdateFormulationAsync(long id, FormulationCommand command, CancellationToken cancellationToken = default) => SaveFormulationAsync(id, command, cancellationToken);

    private async Task<FormulationDto?> SaveFormulationAsync(long? id, FormulationCommand command, CancellationToken cancellationToken)
    {
        ValidateFormulation(command);
        if (await dbContext.Formulations.AnyAsync(x => x.Id != id && x.Code == command.Code.Trim(), cancellationToken)) throw new DomainValidationException("Formulation code already exists.");
        var materialIds = command.Materials.Select(x => x.RawMaterialId).ToArray();
        if (await dbContext.RawMaterials.CountAsync(x => materialIds.Contains(x.Id) && x.IsActive, cancellationToken) != materialIds.Length) throw new DomainValidationException("Every formulation material must reference an active raw material.");
        var now = timeProvider.GetUtcNow().UtcDateTime;
        Formulation entity;
        if (id is null)
        {
            entity = new Formulation { Code = command.Code.Trim(), Name = command.Name.Trim(), BatchSize = command.BatchSize, IsActive = command.IsActive, CreatedAt = now, UpdatedAt = now };
            dbContext.Formulations.Add(entity);
        }
        else
        {
            entity = await dbContext.Formulations.Include(x => x.Materials).SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? null!;
            if (entity is null) return null;
            dbContext.FormulationMaterials.RemoveRange(entity.Materials);
            entity.Materials.Clear(); entity.Code = command.Code.Trim(); entity.Name = command.Name.Trim(); entity.BatchSize = command.BatchSize; entity.IsActive = command.IsActive; entity.UpdatedAt = now;
        }
        entity.Materials = command.Materials.Select(x => new FormulationMaterial { RawMaterialId = x.RawMaterialId, WeightPerBatch = x.WeightPerBatch }).ToList();
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync(id is null ? "Create Formulation" : "Update Formulation", "Formulation", entity.Id, "Success", cancellationToken: cancellationToken);
        return (await GetFormulationAsync(entity.Id, cancellationToken))!;
    }

    private IQueryable<Formulation> FormulationQuery() => dbContext.Formulations.AsNoTracking().Include(x => x.Materials).ThenInclude(x => x.RawMaterial);
    private static RawMaterialDto MapRaw(RawMaterial x) => new(x.Id, x.Code, x.Name, x.Unit, x.CurrentStock, x.ReservedStock, x.LeadTimeDays, x.IsActive, x.RowVersion);
    private static FormulationDto MapFormulation(Formulation x) => new(x.Id, x.Code, x.Name, x.BatchSize, x.IsActive, x.Materials.OrderBy(m => m.RawMaterial.Code).Select(m => new FormulationMaterialDto(m.RawMaterialId, m.RawMaterial.Code, m.WeightPerBatch)).ToArray());
    private static void ValidateRaw(RawMaterialCommand x) { if (string.IsNullOrWhiteSpace(x.Code) || x.Code.Length > 30) throw new DomainValidationException("Material code is required and limited to 30 characters."); if (string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 150) throw new DomainValidationException("Material name is required and limited to 150 characters."); if (string.IsNullOrWhiteSpace(x.Unit) || x.Unit.Length > 30) throw new DomainValidationException("Unit is required."); if (x.CurrentStock < 0 || x.ReservedStock < 0 || x.LeadTimeDays < 0) throw new DomainValidationException("Stock, reserved stock, and lead time cannot be negative."); }
    private static void ValidateFormulation(FormulationCommand x) { if (string.IsNullOrWhiteSpace(x.Code) || x.Code.Length > 30 || string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 150) throw new DomainValidationException("Valid formulation code and name are required."); if (x.BatchSize <= 0 || x.Materials.Count == 0 || x.Materials.Any(m => m.WeightPerBatch <= 0) || x.Materials.Select(m => m.RawMaterialId).Distinct().Count() != x.Materials.Count) throw new DomainValidationException("Formulation materials must be unique with positive weights."); if (x.Materials.Sum(m => m.WeightPerBatch) != x.BatchSize) throw new DomainValidationException("Material weight sum must equal batch size."); }
}
