using AI.Factory.Core.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.RateLimiting;

namespace AI.Factory.Api;

public static class EndpointRegistrationExtensions
{
    public static IEndpointRouteBuilder MapAiFactoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }))
            .AllowAnonymous();

        var auth = endpoints.MapGroup("/api/auth");

        auth.MapGet("/antiforgery", (IAntiforgery antiforgery, HttpContext context) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { token = tokens.RequestToken });
        }).AllowAnonymous();

        auth.MapPost("/login", async (
            [FromForm] LoginRequest request,
            IAuthenticationService authenticationService,
            CancellationToken cancellationToken) =>
        {
            var succeeded = await authenticationService.LoginAsync(request.Username, request.Password, cancellationToken);
            return succeeded
                ? Results.LocalRedirect(NormalizeReturnUrl(request.ReturnUrl))
                : Results.LocalRedirect("/login?error=invalid");
        }).AllowAnonymous()
            .RequireRateLimiting("authentication");

        auth.MapPost("/logout", async (
            [FromForm] LogoutRequest _,
            IAuthenticationService authenticationService,
            CancellationToken cancellationToken) =>
        {
            await authenticationService.LogoutAsync(cancellationToken);
            return Results.LocalRedirect("/login");
        }).RequireAuthorization();

        auth.MapGet("/me", async (
            HttpContext context,
            IAuthenticationService authenticationService,
            CancellationToken cancellationToken) =>
        {
            var user = await authenticationService.GetCurrentUserAsync(context.User, cancellationToken);
            return user is null ? Results.Unauthorized() : Results.Ok(user);
        }).RequireAuthorization();

        var adminUsers = endpoints.MapGroup("/api/admin/users")
            .RequireAuthorization(PolicyNames.CanManageUsers);

        adminUsers.MapGet("/", async (IAdminUserService users, CancellationToken cancellationToken) =>
            Results.Ok(await users.ListAsync(cancellationToken)));

        adminUsers.MapPost("/{userId:long}/activation", async (
            long userId,
            [FromForm] SetActivationRequest request,
            IAdminUserService users,
            CancellationToken cancellationToken) =>
            await users.SetActiveAsync(userId, request.IsActive, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound());

        endpoints.MapMasterDataEndpoints();

        return endpoints;
    }

    private static string NormalizeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//")
            ? returnUrl
            : "/";

}
