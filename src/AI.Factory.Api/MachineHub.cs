using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AI.Factory.Api;

/// <summary>
/// Server-to-client push only (§Module 10 "SignalR"); no client-invokable methods. Every
/// authenticated role may connect ("User ทุก Role ดู SignalR ได้"); reconnection is handled by
/// the Blazor client's HubConnectionBuilder.WithAutomaticReconnect().
/// </summary>
[Authorize]
public sealed class MachineHub : Hub;
