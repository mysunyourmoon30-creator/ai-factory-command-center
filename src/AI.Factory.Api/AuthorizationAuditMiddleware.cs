using AI.Factory.Core.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Factory.Api;

public sealed class AuthorizationAuditMiddleware(RequestDelegate next, ILogger<AuthorizationAuditMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);

        if (context.Response.StatusCode is not (StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden))
        {
            return;
        }

        try
        {
            var auditWriter = context.RequestServices.GetRequiredService<IAuditWriter>();
            await auditWriter.WriteAsync(
                "Unauthorized Access",
                "Endpoint",
                null,
                $"{context.Response.StatusCode} {context.Request.Method} {context.Request.Path}",
                cancellationToken: context.RequestAborted);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to persist unauthorized-access audit for {Path}.", context.Request.Path);
        }
    }
}

public static class AuthorizationAuditMiddlewareExtensions
{
    public static IApplicationBuilder UseAuthorizationAudit(this IApplicationBuilder app) =>
        app.UseMiddleware<AuthorizationAuditMiddleware>();
}
