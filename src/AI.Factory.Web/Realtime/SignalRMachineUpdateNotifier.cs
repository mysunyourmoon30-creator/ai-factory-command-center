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
    /// <summary>
    /// Raised for viewers that live in this process. The Machine Monitoring page is one of them:
    /// it renders on the server, so its old <c>HubConnection</c> was a loopback call from the
    /// server to its own <c>[Authorize]</c> hub with no browser to supply the auth cookie, and
    /// every negotiate returned 401 (finding G7). Subscribing here is the same notification
    /// without the round trip. The hub broadcast below is untouched and still serves any client
    /// that really is remote.
    /// </summary>
    public event Func<MachineDto, Task>? MachineUpdated;

    public async Task NotifyAsync(MachineDto machine, CancellationToken cancellationToken = default)
    {
        await hub.Clients.All.SendAsync("MachineUpdated", machine, cancellationToken);

        var subscribers = MachineUpdated;
        if (subscribers is null) return;

        // Subscribers are Blazor circuits, which can go away between the update and this call.
        // One torn-down circuit must not fail the write that triggered the notification, nor stop
        // the remaining viewers from being told.
        foreach (var subscriber in subscribers.GetInvocationList().Cast<Func<MachineDto, Task>>())
        {
            try { await subscriber(machine); }
            catch (Exception) { /* dead or disposed circuit */ }
        }
    }
}
