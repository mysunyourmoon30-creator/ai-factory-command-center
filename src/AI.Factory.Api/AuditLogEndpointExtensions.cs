using AI.Factory.Core.Audit;
using AI.Factory.Core.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AI.Factory.Api;

public static class AuditLogEndpointExtensions
{
    public static IEndpointRouteBuilder MapAuditLogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/audit-logs", async (
                string? search, string? action, DateTime? fromDate, DateTime? toDate,
                IAuditLogService service, CancellationToken ct, int page = 1, int pageSize = 20) =>
            Results.Ok(await service.ListAsync(new AuditLogQuery(search, action, fromDate, toDate, page, pageSize), ct)))
            .RequireAuthorization(PolicyNames.CanViewAuditLog);

        return endpoints;
    }
}
