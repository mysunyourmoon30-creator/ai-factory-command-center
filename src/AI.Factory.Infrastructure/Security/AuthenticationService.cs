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
            await auditWriter.WriteAsync("Login Failure", "User", user?.Id, user is null ? "Unknown username" : "Account is inactive", username, user?.Id, cancellationToken);
            return false;
        }

        var result = await signInManager.PasswordSignInAsync(user, password, isPersistent: false, lockoutOnFailure: true);
        await auditWriter.WriteAsync(
            result.Succeeded ? "Login Success" : "Login Failure",
            "User",
            user.Id,
            DescribeOutcome(result),
            user.UserName,
            user.Id,
            cancellationToken);

        return result.Succeeded;
    }

    /// <summary>
    /// Every failed sign-in used to be recorded as "Invalid credentials", so the audit log could not
    /// tell a typo from a lockout, a deactivated account, or an unknown username. On the module whose
    /// whole purpose is traceability that loses the signal an investigation actually needs: after a
    /// brute-force run the log showed a row of identical entries, with no indication that the account
    /// had locked - nor that the attempt immediately after it presented the *correct* password and was
    /// refused, which is the single most alarming event the log can hold.
    ///
    /// This changes only what the audit records. The HTTP response is deliberately identical in every
    /// failure case, so no username-enumeration signal is added, and the audit log itself is readable
    /// only by Admin and Manager.
    /// </summary>
    private static string DescribeOutcome(SignInResult result) => result switch
    {
        { Succeeded: true } => "Success",
        { IsLockedOut: true } => "Locked out",
        { IsNotAllowed: true } => "Sign-in not allowed",
        { RequiresTwoFactor: true } => "Requires two-factor",
        _ => "Invalid credentials"
    };

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
