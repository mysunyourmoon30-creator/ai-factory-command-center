using System.Security.Claims;
using AI.Factory.Core.Domain;

namespace AI.Factory.Core.Machines;

public sealed record MachineDto(
    long Id,
    string MachineCode,
    string MachineName,
    MachineRunningStatus RunningStatus,
    decimal Temperature,
    decimal Speed,
    RiskStatus AlertStatus,
    DateTime LastUpdated,
    byte[] RowVersion);

public sealed record SimulateMachineUpdateCommand(MachineRunningStatus RunningStatus, decimal Temperature, decimal Speed, byte[] RowVersion);

public interface IMachineService
{
    Task<IReadOnlyCollection<MachineDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<MachineDto?> SimulateUpdateAsync(long id, SimulateMachineUpdateCommand command, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
}

/// <summary>
/// Notifies live viewers of a machine update. Both the HTTP endpoint and the Blazor page's
/// direct-DI call (per the "UI calls services in-process" rule) go through MachineService, so
/// putting the broadcast here means every caller gets consistent SignalR push behavior, not just
/// HTTP ones. The concrete SignalR implementation lives in AI.Factory.Web - the one project that
/// already references both Infrastructure and the Api-owned Hub - so this stays a plain
/// dependency-free interface in Core.
/// </summary>
public interface IMachineUpdateNotifier
{
    Task NotifyAsync(MachineDto machine, CancellationToken cancellationToken = default);
}

/// <summary>Locked Machine Alert Rule (Master Scope V4, Module 10). Computed server-side only; a client can never send AlertStatus.</summary>
public static class MachineRules
{
    public static RiskStatus CalculateAlertStatus(MachineRunningStatus status, decimal temperature) => status switch
    {
        MachineRunningStatus.Stopped => RiskStatus.Warning,
        MachineRunningStatus.Running when temperature >= 95m => RiskStatus.Critical,
        MachineRunningStatus.Running when temperature >= 85m => RiskStatus.Warning,
        _ => RiskStatus.Normal
    };
}
