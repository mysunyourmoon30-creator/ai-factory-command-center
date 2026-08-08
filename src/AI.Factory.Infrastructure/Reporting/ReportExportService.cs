using AI.Factory.Core.Audit;
using AI.Factory.Core.Domain;
using AI.Factory.Core.Orders;
using AI.Factory.Core.Production;
using AI.Factory.Core.Reporting;
using AI.Factory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.Factory.Infrastructure.Reporting;

public sealed class ReportExportService(
    AppDbContext dbContext,
    IOrderRiskCalculator riskCalculator,
    IMaterialRequirementQueryService requirementQuery,
    TimeProvider timeProvider) : IReportExportService
{
    public async Task<byte[]> ExportProductionRiskCsvAsync(CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.ProductionRiskReport.AsNoTracking()
            .OrderBy(x => x.PlannedCompletionDate).ThenBy(x => x.PlanNumber)
            .ToArrayAsync(cancellationToken);

        return CsvSecurity.WriteCsv(
            ["Plan Number", "Order Number", "Formulation", "Machine", "Required Batch", "Planned Completion", "Lifecycle Status", "Risk Status"],
            rows.Select(x => new string?[]
            {
                x.PlanNumber,
                x.OrderNumber,
                x.FormulationCode,
                x.MachineCode,
                x.RequiredBatch.ToString(),
                x.PlannedCompletionDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                x.PlanStatus.ToString(),
                riskCalculator.Calculate(x.DeliveryDate, x.PlannedCompletionDate).ToString()
            }));
    }

    /// <summary>Reuses the same cumulative-shortage service the screen displays, rather than
    /// re-deriving Module 6.5's multi-date math from the raw-demand view.</summary>
    public async Task<byte[]> ExportMaterialShortageCsvAsync(CancellationToken cancellationToken = default)
    {
        var materials = await requirementQuery.ListAvailabilityAsync(cancellationToken);

        return CsvSecurity.WriteCsv(
            ["Raw Material", "Required Quantity", "On-hand Available", "Eligible Incoming", "Projected Available", "Shortage Quantity", "Material Required Date"],
            materials.Select(x => new string?[]
            {
                $"{x.RawMaterialCode} - {x.RawMaterialName}",
                x.RequiredQuantity.ToString("N3", System.Globalization.CultureInfo.InvariantCulture),
                x.OnHandAvailable.ToString("N3", System.Globalization.CultureInfo.InvariantCulture),
                x.EligibleIncoming.ToString("N3", System.Globalization.CultureInfo.InvariantCulture),
                x.ProjectedAvailable.ToString("N3", System.Globalization.CultureInfo.InvariantCulture),
                x.ShortageQuantity.ToString("N3", System.Globalization.CultureInfo.InvariantCulture),
                x.MaterialRequiredDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            }));
    }

    public async Task<byte[]> ExportPurchaseOrderStatusCsvAsync(CancellationToken cancellationToken = default)
    {
        var today = timeProvider.GetUtcNow().UtcDateTime;
        var rows = await dbContext.PurchaseOrderStatusReport.AsNoTracking()
            .OrderBy(x => x.ExpectedDate).ThenBy(x => x.PurchaseOrderNumber)
            .ToArrayAsync(cancellationToken);

        return CsvSecurity.WriteCsv(
            ["Purchase Order", "Request", "Expected Date", "Received Date", "Status", "Ordered Quantity", "Received Quantity", "Late", "Delay Days"],
            rows.Select(x =>
            {
                var outstanding = x.TotalOrderedQuantity - x.TotalReceivedQuantity;
                var isLate = PurchaseRequestRules.IsLate(x.Status, x.ExpectedDate, today, outstanding);
                return new string?[]
                {
                    x.PurchaseOrderNumber,
                    x.RequestNumber,
                    x.ExpectedDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    x.ReceivedDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    x.Status.ToString(),
                    x.TotalOrderedQuantity.ToString("N3", System.Globalization.CultureInfo.InvariantCulture),
                    x.TotalReceivedQuantity.ToString("N3", System.Globalization.CultureInfo.InvariantCulture),
                    isLate ? "Yes" : "No",
                    PurchaseRequestRules.CalculateDelayDays(x.ExpectedDate, today).ToString()
                };
            }));
    }

    public async Task<byte[]> ExportAuditLogCsvAsync(AuditLogQuery? query = null, CancellationToken cancellationToken = default)
    {
        // Same filters the Audit Log screen applies, so an export matches what the operator is
        // looking at, plus a hard row cap - this view sits on the one table that grows forever.
        var rows = ApplyAuditLogFilter(dbContext.AuditLogReport.AsNoTracking(), query)
            .OrderByDescending(x => x.CreatedAt)
            .Take(IReportExportService.MaxAuditLogExportRows)
            .ToArrayAsync(cancellationToken);

        return BuildAuditLogCsv(await rows);
    }

    private static IQueryable<AuditLogReportRow> ApplyAuditLogFilter(IQueryable<AuditLogReportRow> rows, AuditLogQuery? query)
    {
        if (query is null)
        {
            return rows;
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            rows = rows.Where(x => x.Username.Contains(search) || x.EntityName.Contains(search));
        }
        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            rows = rows.Where(x => x.Action == query.Action);
        }
        if (query.FromDate is not null)
        {
            var from = query.FromDate.Value.Date;
            rows = rows.Where(x => x.CreatedAt >= from);
        }
        if (query.ToDate is not null)
        {
            var to = query.ToDate.Value.Date.AddDays(1);
            rows = rows.Where(x => x.CreatedAt < to);
        }

        return rows;
    }

    private static byte[] BuildAuditLogCsv(AuditLogReportRow[] rows)
    {

        return CsvSecurity.WriteCsv(
            ["Username", "Action", "Entity", "Entity Id", "Result", "Request Id", "Created At"],
            rows.Select(x => new string?[]
            {
                x.Username,
                x.Action,
                x.EntityName,
                x.EntityId?.ToString(),
                x.Result,
                x.RequestId,
                x.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
            }));
    }
}
