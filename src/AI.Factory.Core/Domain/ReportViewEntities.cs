namespace AI.Factory.Core.Domain;

/// <summary>
/// Keyless projections backed by the locked SQL report views (Master Scope V4 §14.7). Each view
/// is a plain JOIN with no time-relative calculation, so it can never disagree with the app's
/// TimeProvider-driven "today"; risk/lateness classification is applied in C# on top of these rows.
/// </summary>
public sealed class ProductionRiskReportRow
{
    public long ProductionPlanId { get; set; }
    public required string PlanNumber { get; set; }
    public required string OrderNumber { get; set; }
    public required string FormulationCode { get; set; }
    public required string MachineCode { get; set; }
    public int RequiredBatch { get; set; }
    public DateTime PlannedCompletionDate { get; set; }
    public ProductionPlanStatus PlanStatus { get; set; }
    public DateTime DeliveryDate { get; set; }
}

public sealed class PurchaseOrderStatusReportRow
{
    public long IncomingPurchaseOrderId { get; set; }
    public required string PurchaseOrderNumber { get; set; }
    public required string RequestNumber { get; set; }
    public DateTime ExpectedDate { get; set; }
    public DateTime? ReceivedDate { get; set; }
    public IncomingPurchaseOrderStatus Status { get; set; }
    public decimal TotalOrderedQuantity { get; set; }
    public decimal TotalReceivedQuantity { get; set; }
}

public sealed class MaterialShortageReportRow
{
    public long ProductionPlanId { get; set; }
    public required string RawMaterialCode { get; set; }
    public required string RawMaterialName { get; set; }
    public required string Unit { get; set; }
    public decimal CurrentStock { get; set; }
    public decimal ReservedStock { get; set; }
    public required string PlanNumber { get; set; }
    public decimal RequiredQuantity { get; set; }
    public DateTime RequiredDate { get; set; }
    public ProductionPlanStatus PlanStatus { get; set; }
}

public sealed class AuditLogReportRow
{
    public long Id { get; set; }
    public long? UserId { get; set; }
    public required string Username { get; set; }
    public required string Action { get; set; }
    public required string EntityName { get; set; }
    public long? EntityId { get; set; }
    public required string Result { get; set; }
    public required string RequestId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }
}
