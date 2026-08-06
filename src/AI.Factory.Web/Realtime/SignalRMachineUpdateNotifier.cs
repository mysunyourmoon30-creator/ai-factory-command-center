using AI.Factory.Api;
using AI.Factory.Core.Machines;
using Microsoft.AspNetCore.SignalR;

namespace AI.Factory.Web.Realtime;

/// <summary>
/// The real notifier, registered after AddInfrastructure to override its no-op default. Lives in
/// Web because it's the only project that already references both Infrastructure (which calls
/// this) and Api (which owns MachineHub) - keeping SignalR out of Infrastructure entirely.
/// </summary>
public sealed class SignalRMachineUpdateNotifier(IHubContext<MachineHub> hub) : IMachineUpdateNotifier
{
    public Task NotifyAsync(MachineDto machine, CancellationToken cancellationToken = default) =>
        hub.Clients.All.SendAsync("MachineUpdated", machine, cancellationToken);
}
