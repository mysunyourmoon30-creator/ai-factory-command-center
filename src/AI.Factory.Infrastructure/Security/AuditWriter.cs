using AI.Factory.Core.Domain;
using AI.Factory.Core.Security;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;

namespace AI.Factory.Infrastructure.Security;

public sealed class AuditWriter(
    AppDbContext dbContext,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider) : IAuditWriter
{
    public async Task WriteAsync(
        string action,
        string entityName,
        long? entityId,
        string result,
        string? username = null,
        long? userId = null,
        CancellationToken cancellationToken = default)
    {
        var context = httpContextAccessor.HttpContext;
        var authenticatedUser = context?.User;

        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = userId ?? ParseUserId(authenticatedUser),
            Username = username ?? authenticatedUser?.Identity?.Name ?? "anonymous",
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            Result = result,
            RequestId = context?.TraceIdentifier ?? Guid.NewGuid().ToString("N"),
            IpAddress = context?.Connection.RemoteIpAddress?.ToString(),
            // Truncated rather than dropped if a client sends something absurd: a header long
            // enough to overflow the column must not be able to fail an audit write, because the
            // write is the thing being relied on.
            UserAgent = Truncate(context?.Request.Headers.UserAgent.ToString(), 512),
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value.Length <= maxLength ? value
        : value[..maxLength];

    private static long? ParseUserId(System.Security.Claims.ClaimsPrincipal? principal)
    {
        var value = principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(value, out var userId) ? userId : null;
    }
}
