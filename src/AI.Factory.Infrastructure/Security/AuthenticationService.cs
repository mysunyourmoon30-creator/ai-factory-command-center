using System.Security.Claims;
using AI.Factory.Core.Security;
using AI.Factory.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace AI.Factory.Infrastructure.Security;

public sealed class AuthenticationService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IAuditWriter auditWriter) : IAuthenticationService
{
    public async Task<bool> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByNameAsync(username);
        if (user is null || !user.IsActive)
        {
            await auditWriter.WriteAsync("Login Failure", "User", user?.Id, "Invalid credentials", username, user?.Id, cancellationToken);
            return false;
        }

        var result = await signInManager.PasswordSignInAsync(user, password, isPersistent: false, lockoutOnFailure: true);
        await auditWriter.WriteAsync(
            result.Succeeded ? "Login Success" : "Login Failure",
            "User",
            user.Id,
            result.Succeeded ? "Success" : "Invalid credentials",
            user.UserName,
            user.Id,
            cancellationToken);

        return result.Succeeded;
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var user = await userManager.GetUserAsync(signInManager.Context.User);
        await signInManager.SignOutAsync();
        await auditWriter.WriteAsync("Logout", "User", user?.Id, "Success", user?.UserName, user?.Id, cancellationToken);
    }

    public async Task<CurrentUser?> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null || !user.IsActive)
        {
            return null;
        }

        var roles = await userManager.GetRolesAsync(user);
        return new CurrentUser(user.Id, user.UserName ?? string.Empty, roles.ToArray());
    }
}
