using System.Security.Claims;
using AI.Factory.Core.Production;
using AI.Factory.Core.Security;
using AI.Factory.Infrastructure.Identity;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AI.Factory.Infrastructure.Security;

public sealed class AdminUserService(
    UserManager<ApplicationUser> userManager,
    AppDbContext dbContext,
    IAuditWriter auditWriter) : IAdminUserService
{
    public async Task<IReadOnlyCollection<DemoUser>> ListAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        EnsureCanManageUsers(actor);

        var users = await userManager.Users.AsNoTracking().OrderBy(x => x.UserName).ToArrayAsync(cancellationToken);

        // One join instead of userManager.GetRolesAsync(...) per user, which issued 1+2N queries.
        var roles = await (from userRole in dbContext.UserRoles.AsNoTracking()
                           join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                           select new { userRole.UserId, RoleName = role.Name })
            .ToArrayAsync(cancellationToken);

        var rolesByUser = roles
            .GroupBy(x => x.UserId)
            .ToDictionary(x => x.Key, x => x.Select(r => r.RoleName ?? string.Empty).ToArray());

        return users
            .Select(user => new DemoUser(
                user.Id,
                user.UserName ?? string.Empty,
                user.IsActive,
                rolesByUser.TryGetValue(user.Id, out var userRoles) ? userRoles : []))
            .ToArray();
    }

    public async Task<bool> SetActiveAsync(ClaimsPrincipal actor, long userId, bool isActive, CancellationToken cancellationToken = default)
    {
        EnsureCanManageUsers(actor);

        // Deactivation now revokes the account's live session at the next security-stamp
        // revalidation, so an Admin doing this to themselves locks themselves out - and if they are
        // the only Admin, locks everyone out of user management permanently, with no in-app way
        // back. Another Admin can still deactivate them; only self-deactivation is refused.
        if (!isActive && CurrentUserId(actor) == userId)
        {
            throw new BusinessConflictException("You cannot deactivate your own account. Ask another Admin to do it.");
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        user.IsActive = isActive;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            // Shaped like every other service's refusal so the write-path convention can map it
            // (409, not 500) and the Blazor page can show it. It used to be an
            // InvalidOperationException, which no caller caught: on the admin screen - the one
            // with the strongest privileges - a failed update tore down the circuit.
            throw new BusinessConflictException(string.Join("; ", result.Errors.Select(x => x.Description)));
        }

        // Deactivating only blocked *new* sign-ins: LoginAsync checks IsActive, but authorization
        // on an already-issued cookie never re-reads the user, so a deactivated account kept full
        // access for the rest of its 8-hour sliding window. Setting IsActive does not rotate the
        // security stamp on its own, so the cookie stayed valid no matter what the validator did.
        // Rotating it here is what actually revokes the live session, at the next
        // SecurityStampValidatorOptions.ValidationInterval.
        if (!isActive)
        {
            var stampResult = await userManager.UpdateSecurityStampAsync(user);
            if (!stampResult.Succeeded)
            {
                throw new BusinessConflictException(string.Join("; ", stampResult.Errors.Select(x => x.Description)));
            }
        }

        await auditWriter.WriteAsync("Activate / Deactivate User", "User", user.Id, isActive ? "Activated" : "Deactivated", cancellationToken: cancellationToken);
        return true;
    }

    private static long? CurrentUserId(ClaimsPrincipal actor) =>
        long.TryParse(actor.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    // The Blazor UI calls this service in-process, so the endpoint's CanManageUsers policy never
    // runs on that path - the service has to re-check for itself, same as every other mutating
    // service (see MachineService.EnsureCanSimulate).

    private static void EnsureCanManageUsers(ClaimsPrincipal actor)
    {
        if (!actor.IsInRole(RoleNames.Admin))
            throw new UnauthorizedAccessException("Admin role is required to manage demo users.");
    }
}
