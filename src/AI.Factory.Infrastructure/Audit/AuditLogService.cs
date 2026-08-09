using AI.Factory.Core.Audit;
using AI.Factory.Core.Domain;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.Factory.Infrastructure.Audit;

public sealed class AuditLogService(AppDbContext dbContext) : IAuditLogService
{
    public async Task<AuditLogPage> ListAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var logs = dbContext.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            // RequestId is included so an operator can pull every row belonging to one request -
            // the point of having a correlation id at all. Kept in step with the identical clause
            // in ReportExportService.ApplyAuditLogFilter, which filters the report *view*; the two
            // are separate types and so cannot share the expression.
            logs = logs.Where(x => x.Username.Contains(search) || x.EntityName.Contains(search) || x.RequestId.Contains(search));
        }
        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            logs = logs.Where(x => x.Action == query.Action);
        }
        if (query.FromDate is not null)
        {
            var from = query.FromDate.Value.Date;
            logs = logs.Where(x => x.CreatedAt >= from);
        }
        if (query.ToDate is not null)
        {
            var to = query.ToDate.Value.Date.AddDays(1);
            logs = logs.Where(x => x.CreatedAt < to);
        }

        var totalCount = await logs.CountAsync(cancellationToken);
        var entities = await logs.OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);

        return new AuditLogPage(entities.Select(Map).ToArray(), page, pageSize, totalCount);
    }

    private static AuditLogDto Map(AuditLog x) =>
        new(x.Id, x.UserId, x.Username, x.Action, x.EntityName, x.EntityId, x.Result, x.RequestId, x.IpAddress, x.UserAgent, x.CreatedAt);
}
