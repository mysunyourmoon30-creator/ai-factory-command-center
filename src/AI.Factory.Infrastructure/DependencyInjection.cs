using AI.Factory.Core.Time;
using AI.Factory.Infrastructure.Identity;
using AI.Factory.Infrastructure.Persistence;
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
            .AddEntityFrameworkStores<AppDbContext>();

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
