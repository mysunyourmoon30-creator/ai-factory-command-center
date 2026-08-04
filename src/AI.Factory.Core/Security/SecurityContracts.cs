using System.Security.Claims;

namespace AI.Factory.Core.Security;

public static class RoleNames
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Planner = "Planner";
    public const string Viewer = "Viewer";

    public static readonly string[] All = [Admin, Manager, Planner, Viewer];
}

public static class PolicyNames
{
    public const string CanManageMasterData = nameof(CanManageMasterData);
    public const string CanManageOrders = nameof(CanManageOrders);
    public const string CanManageProductionPlans = nameof(CanManageProductionPlans);
    public const string CanCreatePurchaseRequest = nameof(CanCreatePurchaseRequest);
    public const string CanApprovePurchaseRequest = nameof(CanApprovePurchaseRequest);
    public const string CanRecordIncomingPurchaseOrder = nameof(CanRecordIncomingPurchaseOrder);
    public const string CanUseAiCopilot = nameof(CanUseAiCopilot);
    public const string CanViewAuditLog = nameof(CanViewAuditLog);
    public const string CanManageUsers = nameof(CanManageUsers);
    public const string CanUpdateMachineSimulator = nameof(CanUpdateMachineSimulator);
    public const string CanExportReports = nameof(CanExportReports);
}

public sealed record CurrentUser(long Id, string Username, IReadOnlyCollection<string> Roles);
public sealed record DemoUser(long Id, string Username, bool IsActive, IReadOnlyCollection<string> Roles);

public interface IAuthenticationService
{
    Task<bool> LoginAsync(string username, string password, CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
    Task<CurrentUser?> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}

public interface IAdminUserService
{
    Task<IReadOnlyCollection<DemoUser>> ListAsync(CancellationToken cancellationToken = default);
    Task<bool> SetActiveAsync(long userId, bool isActive, CancellationToken cancellationToken = default);
}

public interface IAuditWriter
{
    Task WriteAsync(
        string action,
        string entityName,
        long? entityId,
        string result,
        string? username = null,
        long? userId = null,
        CancellationToken cancellationToken = default);
}
