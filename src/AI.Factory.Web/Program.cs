using AI.Factory.Api;
using AI.Factory.Infrastructure;
using AI.Factory.Infrastructure.Identity;
using AI.Factory.Infrastructure.Persistence;
using AI.Factory.Infrastructure.MasterData;
using AI.Factory.Infrastructure.Orders;
using AI.Factory.Infrastructure.Production;
using AI.Factory.Web.Components;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);

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
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "AI.Factory.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddRateLimiter(options => options.AddPolicy("authentication", context =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        })));
builder.Services.AddAiFactoryAuthorization();

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
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseHttpsRedirection();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorizationAudit();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapAiFactoryEndpoints();

app.Run();

public partial class Program;
