using System.Security.Claims;
using AI.Factory.Core.Security;
using AI.Factory.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AI.Factory.Infrastructure.Security;

public sealed class AdminUserService(
    UserManager<ApplicationUser> userManager,
    IAuditWriter auditWriter) : IAdminUserService
{
    public async Task<IReadOnlyCollection<DemoUser>> ListAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        EnsureCanManageUsers(actor);

        var users = await userManager.Users.AsNoTracking().OrderBy(x => x.UserName).ToArrayAsync(cancellationToken);
        var result = new List<DemoUser>(users.Length);

        foreach (var user in users)
        {
            result.Add(new DemoUser(user.Id, user.UserName ?? string.Empty, user.IsActive, (await userManager.GetRolesAsync(user)).ToArray()));
        }

        return result;
    }

    public async Task<bool> SetActiveAsync(ClaimsPrincipal actor, long userId, bool isActive, CancellationToken cancellationToken = default)
    {
        EnsureCanManageUsers(actor);

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        user.IsActive = isActive;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
        }

        await auditWriter.WriteAsync("Activate / Deactivate User", "User", user.Id, isActive ? "Activated" : "Deactivated", cancellationToken: cancellationToken);
        return true;
    }

    // The Blazor UI calls this service in-process, so the endpoint's CanManageUsers policy never
    // runs on that path - the service has to re-check for itself, same as every other mutating
    // service (see MachineService.EnsureCanSimulate).
    private static void EnsureCanManageUsers(ClaimsPrincipal actor)
    {
        if (!actor.IsInRole(RoleNames.Admin))
            throw new UnauthorizedAccessException("Admin role is required to manage demo users.");
    }
}
