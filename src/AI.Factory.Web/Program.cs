using AI.Factory.Api;
using AI.Factory.Infrastructure;
using AI.Factory.Infrastructure.Identity;
using AI.Factory.Infrastructure.Persistence;
using AI.Factory.Infrastructure.MasterData;
using AI.Factory.Infrastructure.Orders;
using AI.Factory.Infrastructure.Production;
using AI.Factory.Core.Machines;
using AI.Factory.Web.Components;
using AI.Factory.Web.Realtime;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options =>
{
    options.Cookie.Name = "AI.Factory.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/forbidden";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context =>
    {
        if (IsMachineReadablePath(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (IsMachineReadablePath(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});
// AddIdentityCookies() already points OnValidatePrincipal at SecurityStampValidator, and the
// Configure above mutates the existing Events instance rather than replacing it, so that hook
// survives. What was missing was anything to validate *against*: deactivating a user did not
// rotate their security stamp, so the check always passed. AdminUserService now rotates it, and
// this states the revocation window explicitly rather than inheriting a framework default that
// could change underneath us.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.FromMinutes(30));
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "AI.Factory.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("authentication", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("ai-copilot", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimits:AiCopilotPermitLimit", 10),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});
builder.Services.AddAiFactoryAuthorization();
builder.Services.AddSignalR();
// Still registered after AddInfrastructure, so it still overrides the no-op default - that order
// is what matters and has not changed. Two lines rather than one because the Machine Monitoring
// page resolves the concrete type to subscribe to its MachineUpdated event, and both registrations
// must hand back the same singleton instance.
builder.Services.AddSingleton<SignalRMachineUpdateNotifier>();
builder.Services.AddSingleton<IMachineUpdateNotifier>(services => services.GetRequiredService<SignalRMachineUpdateNotifier>());

var app = builder.Build();

var seedIdentity = args.Contains("--seed-identity", StringComparer.OrdinalIgnoreCase);
var seedMasterData = args.Contains("--seed-master-data", StringComparer.OrdinalIgnoreCase);
var seedCustomerOrders = args.Contains("--seed-customer-orders", StringComparer.OrdinalIgnoreCase);
var seedProductionPlans = args.Contains("--seed-production-plans", StringComparer.OrdinalIgnoreCase);
if (seedIdentity || seedMasterData || seedCustomerOrders || seedProductionPlans)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
    await DemoIdentitySeeder.SeedAsync(scope.ServiceProvider);
    if (seedMasterData || seedCustomerOrders || seedProductionPlans)
    {
        await CanonicalMasterDataSeeder.SeedAsync(scope.ServiceProvider);
    }
    if (seedCustomerOrders || seedProductionPlans)
    {
        await CanonicalCustomerOrderSeeder.SeedAsync(scope.ServiceProvider);
    }
    if (seedProductionPlans)
    {
        await CanonicalProductionPlanSeeder.SeedAsync(scope.ServiceProvider);
        await CanonicalProcurementSeeder.SeedAsync(scope.ServiceProvider);
    }
    return;
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var isAntiforgeryFailure = error is AntiforgeryValidationException ||
            error is InvalidOperationException invalidOperation &&
            invalidOperation.Message.Contains("invalid anti-forgery token", StringComparison.OrdinalIgnoreCase);
        var statusCode = isAntiforgeryFailure
            ? StatusCodes.Status400BadRequest
            : StatusCodes.Status500InternalServerError;

        await Results.Problem(
            statusCode: statusCode,
            title: isAntiforgeryFailure ? "Invalid antiforgery token" : "An unexpected error occurred")
            .ExecuteAsync(context);
    }));
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
// First in the pipeline so every response carries the headers, including static assets and
// error/status-code responses produced further down.
app.UseSecurityHeaders();
app.UseWhen(
    context => !IsMachineReadablePath(context.Request.Path),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseHttpsRedirection();
app.UseSerilogRequestLogging();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorizationAudit();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapAiFactoryEndpoints();
app.MapHub<MachineHub>("/hubs/machines");

app.Run();

static bool IsMachineReadablePath(PathString path) =>
    path.StartsWithSegments("/api") || path.StartsWithSegments("/health") || path.StartsWithSegments("/hubs");

public partial class Program;
