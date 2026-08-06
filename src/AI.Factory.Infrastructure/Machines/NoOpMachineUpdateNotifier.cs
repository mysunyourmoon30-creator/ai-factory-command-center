using AI.Factory.Core.Machines;

namespace AI.Factory.Infrastructure.Machines;

/// <summary>
/// Default registration so MachineService is fully resolvable without any SignalR dependency in
/// Infrastructure (wrong reference direction - the Hub lives in Api). AI.Factory.Web overrides
/// this with the real SignalR-backed notifier after calling AddInfrastructure.
/// </summary>
public sealed class NoOpMachineUpdateNotifier : IMachineUpdateNotifier
{
    public Task NotifyAsync(MachineDto machine, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
