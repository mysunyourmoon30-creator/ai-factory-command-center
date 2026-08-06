using AI.Factory.Infrastructure.Identity;
using AI.Factory.Infrastructure.Persistence;
using AI.Factory.Infrastructure.MasterData;
using AI.Factory.Infrastructure.Orders;
using AI.Factory.Core.Copilot;
using AI.Factory.Core.Time;
using AI.Factory.Infrastructure.Production;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AI.Factory.IntegrationTests;

public sealed class AiFactoryWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"AI.Factory.Tests.{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            [new KeyValuePair<string, string?>("RateLimits:AiCopilotPermitLimit", "1000")]));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<TimeProvider>();
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero)));
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<IOllamaClient>();
            services.AddScoped<IOllamaClient, FakeOllamaClient>();
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Database.EnsureCreated();
        DemoIdentitySeeder.SeedAsync(scope.ServiceProvider).GetAwaiter().GetResult();
        CanonicalMasterDataSeeder.SeedAsync(scope.ServiceProvider).GetAwaiter().GetResult();
        CanonicalCustomerOrderSeeder.SeedAsync(scope.ServiceProvider).GetAwaiter().GetResult();
        CanonicalProductionPlanSeeder.SeedAsync(scope.ServiceProvider).GetAwaiter().GetResult();
        CanonicalProcurementSeeder.SeedAsync(scope.ServiceProvider).GetAwaiter().GetResult();
        return host;
    }
}
