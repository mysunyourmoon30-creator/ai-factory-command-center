namespace AI.Factory.Core.Audit;

public sealed record AuditLogDto(
    long Id,
    long? UserId,
    string Username,
    string Action,
    string EntityName,
    long? EntityId,
    string Result,
    string RequestId,
    string? IpAddress,
    string? UserAgent,
    DateTime CreatedAt);

public sealed record AuditLogPage(IReadOnlyCollection<AuditLogDto> Items, int Page, int PageSize, int TotalCount);
public sealed record AuditLogQuery(string? Search = null, string? Action = null, DateTime? FromDate = null, DateTime? ToDate = null, int Page = 1, int PageSize = 20);

/// <summary>Read-only counterpart to IAuditWriter, which is write-only and append-only (§Module 11).</summary>
public interface IAuditLogService
{
    Task<AuditLogPage> ListAsync(AuditLogQuery query, CancellationToken cancellationToken = default);
}
