using AI.Factory.Core.Security;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Factory.Api;

public static class AuthorizationRegistrationExtensions
{
    public static IServiceCollection AddAiFactoryAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(PolicyNames.CanManageMasterData, policy => policy.RequireRole(RoleNames.Admin, RoleNames.Planner));
            options.AddPolicy(PolicyNames.CanManageOrders, policy => policy.RequireRole(RoleNames.Admin, RoleNames.Planner));
            options.AddPolicy(PolicyNames.CanManageProductionPlans, policy => policy.RequireRole(RoleNames.Admin, RoleNames.Planner));
            options.AddPolicy(PolicyNames.CanCreatePurchaseRequest, policy => policy.RequireRole(RoleNames.Admin, RoleNames.Manager, RoleNames.Planner));
            options.AddPolicy(PolicyNames.CanApprovePurchaseRequest, policy => policy.RequireRole(RoleNames.Admin, RoleNames.Manager));
            options.AddPolicy(PolicyNames.CanRecordIncomingPurchaseOrder, policy => policy.RequireRole(RoleNames.Admin, RoleNames.Manager));
            options.AddPolicy(PolicyNames.CanUseAiCopilot, policy => policy.RequireRole(RoleNames.All));
            options.AddPolicy(PolicyNames.CanViewAuditLog, policy => policy.RequireRole(RoleNames.Admin, RoleNames.Manager));
            options.AddPolicy(PolicyNames.CanManageUsers, policy => policy.RequireRole(RoleNames.Admin));
            options.AddPolicy(PolicyNames.CanUpdateMachineSimulator, policy => policy.RequireRole(RoleNames.Admin));
            options.AddPolicy(PolicyNames.CanExportReports, policy => policy.RequireRole(RoleNames.All));
        });

        return services;
    }
}
