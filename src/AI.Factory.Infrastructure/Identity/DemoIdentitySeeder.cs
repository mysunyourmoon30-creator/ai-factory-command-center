using AI.Factory.Core.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Factory.Infrastructure.Identity;

public static class DemoIdentitySeeder
{
    public const string DemoPassword = "Demo@12345";

    private static readonly (string Username, string Role)[] Users =
    [
        ("admin.demo", RoleNames.Admin),
        ("manager.demo", RoleNames.Manager),
        ("planner.demo", RoleNames.Planner),
        ("viewer.demo", RoleNames.Viewer)
    ];

    public static async Task SeedAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole<long>>>();
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var roleName in RoleNames.All)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var roleResult = await roleManager.CreateAsync(new IdentityRole<long>(roleName));
                EnsureSucceeded(roleResult);
            }
        }

        foreach (var (username, role) in Users)
        {
            var user = await userManager.FindByNameAsync(username);
            if (user is null)
            {
                user = new ApplicationUser { UserName = username, IsActive = true };
                EnsureSucceeded(await userManager.CreateAsync(user, DemoPassword));
            }

            if (!await userManager.IsInRoleAsync(user, role))
            {
                EnsureSucceeded(await userManager.AddToRoleAsync(user, role));
            }
        }
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
        }
    }
}
