using AI.Factory.Core.Time;
using AI.Factory.Infrastructure.Identity;
using AI.Factory.Infrastructure.Persistence;
using AI.Factory.Core.Security;
using AI.Factory.Infrastructure.Security;
using AI.Factory.Core.MasterData;
using AI.Factory.Infrastructure.MasterData;
using AI.Factory.Core.Orders;
using AI.Factory.Infrastructure.Orders;
using AI.Factory.Core.Production;
using AI.Factory.Infrastructure.Production;
using AI.Factory.Core.Reporting;
using AI.Factory.Infrastructure.Reporting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Factory.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName)
    {
        var connectionString = configuration.GetConnectionString("AiFactory")
            ?? Environment.GetEnvironmentVariable("AI_FACTORY_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:AiFactory or AI_FACTORY_CONNECTION_STRING.");

        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
        services.AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = false)
            .AddRoles<IdentityRole<long>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager();

        services.AddHttpContextAccessor();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<IMasterDataService, MasterDataService>();
        services.AddScoped<ICustomerOrderService, CustomerOrderService>();
        services.AddScoped<IProductionPlanService, ProductionPlanService>();
        services.AddScoped<IMaterialRequirementQueryService, MaterialRequirementQueryService>();
        services.AddScoped<IMaterialShortageService, MaterialShortageService>();
        services.AddScoped<IProcurementService, ProcurementService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IReportExportService, ReportExportService>();
        services.AddSingleton<IOrderRiskCalculator, OrderRiskCalculator>();

        services.AddSingleton(CreateTimeProvider(configuration, environmentName));
        return services;
    }

    private static TimeProvider CreateTimeProvider(IConfiguration configuration, string environmentName)
    {
        if (!environmentName.Equals("Demo", StringComparison.OrdinalIgnoreCase))
        {
            return TimeProvider.System;
        }

        var configuredUtc = configuration["Demo:FixedUtc"];
        if (string.IsNullOrWhiteSpace(configuredUtc))
        {
            throw new InvalidOperationException("Demo:FixedUtc must be set to canonical seed time T before accepting requests.");
        }

        return new FixedTimeProvider(DateTimeOffset.Parse(configuredUtc, null, System.Globalization.DateTimeStyles.AssumeUniversal));
    }
}
