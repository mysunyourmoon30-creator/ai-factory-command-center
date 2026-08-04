namespace AI.Factory.Core.MasterData;

public sealed record RawMaterialDto(long Id, string Code, string Name, string Unit, decimal CurrentStock, decimal ReservedStock, int LeadTimeDays, bool IsActive, byte[] RowVersion);
public sealed record RawMaterialCommand(string Code, string Name, string Unit, decimal CurrentStock, decimal ReservedStock, int LeadTimeDays, bool IsActive, byte[]? RowVersion = null);
public sealed record FormulationMaterialDto(long RawMaterialId, string RawMaterialCode, decimal WeightPerBatch);
public sealed record FormulationDto(long Id, string Code, string Name, decimal BatchSize, bool IsActive, IReadOnlyCollection<FormulationMaterialDto> Materials);
public sealed record FormulationMaterialCommand(long RawMaterialId, decimal WeightPerBatch);
public sealed record FormulationCommand(string Code, string Name, decimal BatchSize, bool IsActive, IReadOnlyCollection<FormulationMaterialCommand> Materials);

public interface IMasterDataService
{
    Task<IReadOnlyCollection<RawMaterialDto>> ListRawMaterialsAsync(CancellationToken cancellationToken = default);
    Task<RawMaterialDto?> GetRawMaterialAsync(long id, CancellationToken cancellationToken = default);
    Task<RawMaterialDto> CreateRawMaterialAsync(RawMaterialCommand command, CancellationToken cancellationToken = default);
    Task<RawMaterialDto?> UpdateRawMaterialAsync(long id, RawMaterialCommand command, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<FormulationDto>> ListFormulationsAsync(CancellationToken cancellationToken = default);
    Task<FormulationDto?> GetFormulationAsync(long id, CancellationToken cancellationToken = default);
    Task<FormulationDto> CreateFormulationAsync(FormulationCommand command, CancellationToken cancellationToken = default);
    Task<FormulationDto?> UpdateFormulationAsync(long id, FormulationCommand command, CancellationToken cancellationToken = default);
}

public sealed class DomainValidationException(string message) : Exception(message);
public sealed class ConcurrencyConflictException(string message) : Exception(message);
