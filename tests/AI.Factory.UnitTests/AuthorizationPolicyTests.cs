using System.Security.Claims;
using AI.Factory.Api;
using AI.Factory.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Factory.UnitTests;

public sealed class AuthorizationPolicyTests
{
    [Theory]
    [InlineData(RoleNames.Planner, PolicyNames.CanManageProductionPlans, true)]
    [InlineData(RoleNames.Planner, PolicyNames.CanApprovePurchaseRequest, false)]
    [InlineData(RoleNames.Manager, PolicyNames.CanApprovePurchaseRequest, true)]
    [InlineData(RoleNames.Viewer, PolicyNames.CanManageUsers, false)]
    [InlineData(RoleNames.Manager, PolicyNames.CanUpdateMachineSimulator, false)]
    [InlineData(RoleNames.Admin, PolicyNames.CanUpdateMachineSimulator, true)]
    public async Task Role_matrix_matches_locked_policy(string role, string policy, bool expected)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAiFactoryAuthorization();
        await using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Name, $"{role.ToLowerInvariant()}.demo"),
            new Claim(ClaimTypes.Role, role)
        ], "Test"));

        var result = await authorization.AuthorizeAsync(principal, null, policy);
        Assert.Equal(expected, result.Succeeded);
    }
}
