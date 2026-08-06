namespace AI.Factory.Core.Reporting;

/// <summary>
/// CSV export backed by the locked report views (§14.7). Production Risk and Purchase Order
/// Status are naturally one row per entity, so their CSV reads straight from the view with
/// risk/lateness classification applied in C#. Material Shortage is a cumulative, multi-row
/// calculation (Module 6.5); its CSV reuses IMaterialRequirementQueryService directly so the
/// exported numbers can never disagree with what the screen shows. vw_MaterialShortageReport
/// still exists as the locked, independently query-able raw-demand reporting surface.
/// </summary>
public interface IReportExportService
{
    Task<byte[]> ExportProductionRiskCsvAsync(CancellationToken cancellationToken = default);
    Task<byte[]> ExportMaterialShortageCsvAsync(CancellationToken cancellationToken = default);
    Task<byte[]> ExportPurchaseOrderStatusCsvAsync(CancellationToken cancellationToken = default);
    Task<byte[]> ExportAuditLogCsvAsync(CancellationToken cancellationToken = default);
}
