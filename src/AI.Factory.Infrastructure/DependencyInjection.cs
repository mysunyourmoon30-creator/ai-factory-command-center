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
using AI.Factory.Core.Copilot;
using AI.Factory.Infrastructure.Copilot;
using AI.Factory.Core.Alerts;
using AI.Factory.Infrastructure.Alerts;
using AI.Factory.Core.Machines;
using AI.Factory.Infrastructure.Machines;
using AI.Factory.Core.Audit;
using AI.Factory.Infrastructure.Audit;
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

        services.AddScoped<IAiTool, MaterialShortageAiTool>();
        services.AddScoped<IAiTool, DelayedProductionOrderAiTool>();
        services.AddScoped<IAiTool, LatePurchaseOrderAiTool>();
        services.AddScoped<IAiTool, DailyFactorySummaryAiTool>();
        services.AddScoped<ICopilotService, CopilotService>();
        services.AddSingleton(CreateOllamaHttpClient(configuration));
        services.AddScoped<IOllamaClient, OllamaClient>();
        services.AddScoped<IReadinessService, ReadinessService>();

        services.AddScoped<IAlertEvaluationService, AlertEvaluationService>();
        services.AddScoped<IMachineService, MachineService>();
        services.AddSingleton<IMachineUpdateNotifier, NoOpMachineUpdateNotifier>();
        services.AddScoped<IAuditLogService, AuditLogService>();

        services.AddSingleton(CreateTimeProvider(configuration, environmentName));
        return services;
    }

    /// <summary>
    /// One long-lived HttpClient for the single well-known Ollama base address (Microsoft's own
    /// guidance: socket exhaustion comes from many short-lived clients, not one singleton).
    /// Refuses a non-localhost base URL at startup (§10.10 "Localhost-only Ollama").
    /// </summary>
    private static HttpClient CreateOllamaHttpClient(IConfiguration configuration)
    {
        var baseUrl = configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
        var baseUri = new Uri(baseUrl, UriKind.Absolute);
        if (baseUri.Host is not ("localhost" or "127.0.0.1" or "::1"))
        {
            throw new InvalidOperationException("Ollama:BaseUrl must be localhost-only.");
        }

        return new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(30) };
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
