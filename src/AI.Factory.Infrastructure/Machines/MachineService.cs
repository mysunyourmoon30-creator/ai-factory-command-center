using System.Security.Claims;
using AI.Factory.Core.Alerts;
using AI.Factory.Core.Machines;
using AI.Factory.Core.MasterData;
using AI.Factory.Core.Security;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.Factory.Infrastructure.Machines;

public sealed class MachineService(
    AppDbContext dbContext,
    TimeProvider timeProvider,
    IAlertEvaluationService alertEvaluation,
    IMachineUpdateNotifier updateNotifier) : IMachineService
{
    public async Task<IReadOnlyCollection<MachineDto>> ListAsync(CancellationToken cancellationToken = default) =>
        (await dbContext.Machines.AsNoTracking().OrderBy(x => x.MachineCode).ToArrayAsync(cancellationToken))
            .Select(Map).ToArray();

    public async Task<MachineDto?> SimulateUpdateAsync(long id, SimulateMachineUpdateCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        EnsureCanSimulate(actor);
        Validate(command);

        var machine = await dbContext.Machines.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (machine is null) return null;
        EnsureRowVersion(machine.RowVersion, command.RowVersion);

        dbContext.Entry(machine).Property(x => x.RowVersion).OriginalValue = command.RowVersion;
        machine.RunningStatus = command.RunningStatus;
        machine.Temperature = command.Temperature;
        machine.Speed = command.Speed;
        machine.AlertStatus = MachineRules.CalculateAlertStatus(command.RunningStatus, command.Temperature);
        machine.LastUpdated = timeProvider.GetUtcNow().UtcDateTime;

        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException("Machine changed; reload before saving."); }

        await alertEvaluation.EvaluateAsync(cancellationToken);
        var dto = Map(machine);
        await updateNotifier.NotifyAsync(dto, cancellationToken);
        return dto;
    }

    private static MachineDto Map(Core.Domain.Machine x) =>
        new(x.Id, x.MachineCode, x.MachineName, x.RunningStatus, x.Temperature, x.Speed, x.AlertStatus, x.LastUpdated, x.RowVersion);

    private static void Validate(SimulateMachineUpdateCommand command)
    {
        if (command.Temperature < 0) throw new DomainValidationException("Temperature may not be negative.");
        if (command.Speed < 0) throw new DomainValidationException("Speed may not be negative.");
    }

    private static void EnsureRowVersion(byte[] current, byte[] submitted)
    {
        if (submitted is null || !current.SequenceEqual(submitted))
            throw new ConcurrencyConflictException("Machine changed; reload before saving.");
    }

    private static void EnsureCanSimulate(ClaimsPrincipal actor)
    {
        if (!actor.IsInRole(RoleNames.Admin))
            throw new UnauthorizedAccessException("Admin role is required to simulate a machine update.");
    }
}
