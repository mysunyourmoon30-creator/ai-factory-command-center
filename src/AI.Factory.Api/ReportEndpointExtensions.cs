using AI.Factory.Core.Audit;
using AI.Factory.Core.Production;
using AI.Factory.Core.Reporting;
using AI.Factory.Core.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AI.Factory.Api;

public static class ReportEndpointExtensions
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var dashboard = endpoints.MapGroup("/api/dashboard").RequireAuthorization();
        dashboard.MapGet("/kpi", async (IDashboardService service, CancellationToken ct) => Results.Ok(await service.GetKpiAsync(ct)));
        dashboard.MapGet("/daily-summary", async (IDashboardService service, CancellationToken ct) => Results.Ok(await service.GetDailySummaryAsync(ct)));
        dashboard.MapGet("/critical-risks", async (IDashboardService service, CancellationToken ct) => Results.Ok(await service.GetCriticalRisksAsync(ct)));
        dashboard.MapGet("/alerts", async (IDashboardService service, CancellationToken ct) => Results.Ok(await service.ListActiveAlertsAsync(ct)));

        // Bare RequireAuthorization, like every other read group: the overview aggregates figures
        // each role can already read on /orders, /procurement, /material-shortages and
        // /machine-monitoring, so gating it would add a twelfth named policy to protect nothing.
        var executive = endpoints.MapGroup("/api/executive").RequireAuthorization();
        executive.MapGet("/overview", async (IExecutiveService service, CancellationToken ct) => Results.Ok(await service.GetOverviewAsync(ct)));

        var reports = endpoints.MapGroup("/api/reports").RequireAuthorization();
        reports.MapGet("/production-risk", async (IProductionPlanService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)));
        reports.MapGet("/material-shortage", async (IMaterialShortageService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)));
        reports.MapGet("/purchase-order-status", async (IProcurementService service, CancellationToken ct) => Results.Ok(await service.ListIncomingPurchaseOrdersAsync(ct)));

        var exports = reports.MapGroup("").RequireAuthorization(PolicyNames.CanExportReports);
        exports.MapGet("/production-risk/export.csv", async (IReportExportService service, CancellationToken ct) =>
            Results.File(await service.ExportProductionRiskCsvAsync(ct), "text/csv; charset=utf-8", "production-risk-report.csv"));
        exports.MapGet("/material-shortage/export.csv", async (IReportExportService service, CancellationToken ct) =>
            Results.File(await service.ExportMaterialShortageCsvAsync(ct), "text/csv; charset=utf-8", "material-shortage-report.csv"));
        exports.MapGet("/purchase-order-status/export.csv", async (IReportExportService service, CancellationToken ct) =>
            Results.File(await service.ExportPurchaseOrderStatusCsvAsync(ct), "text/csv; charset=utf-8", "purchase-order-status-report.csv"));

        // Audit Log Report export is gated by CanViewAuditLog (Admin+Manager), not the generic
        // CanExportReports (all four roles) — nobody should export a log they can't view.
        // Filters mirror the Audit Log screen's own, so "Export CSV" returns what the operator is
        // looking at. All are optional: with none supplied the export is the newest rows overall,
        // capped by IReportExportService.MaxAuditLogExportRows rather than the whole table.
        reports.MapGet("/audit-log/export.csv", async (
                IReportExportService service,
                string? search,
                string? action,
                DateTime? fromDate,
                DateTime? toDate,
                CancellationToken ct) =>
                Results.File(
                    await service.ExportAuditLogCsvAsync(new AuditLogQuery(search, action, fromDate, toDate), ct),
                    "text/csv; charset=utf-8",
                    "audit-log-report.csv"))
            .RequireAuthorization(PolicyNames.CanViewAuditLog);

        return endpoints;
    }
}
