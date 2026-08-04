namespace AI.Factory.Core.Domain;

public sealed class RawMaterial
{
    public long Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Unit { get; set; }
    public decimal CurrentStock { get; set; }
    public decimal ReservedStock { get; set; }
    public int LeadTimeDays { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class Formulation
{
    public long Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public decimal BatchSize { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<FormulationMaterial> Materials { get; set; } = [];
}

public sealed class FormulationMaterial
{
    public long Id { get; set; }
    public long FormulationId { get; set; }
    public Formulation Formulation { get; set; } = null!;
    public long RawMaterialId { get; set; }
    public RawMaterial RawMaterial { get; set; } = null!;
    public decimal WeightPerBatch { get; set; }
}

public sealed class CustomerOrder
{
    public long Id { get; set; }
    public required string OrderNumber { get; set; }
    public required string CustomerName { get; set; }
    public long FormulationId { get; set; }
    public Formulation Formulation { get; set; } = null!;
    public decimal Quantity { get; set; }
    public DateTime DeliveryDate { get; set; }
    public required string Priority { get; set; }
    public CustomerOrderStatus Status { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ProductionPlan? ProductionPlan { get; set; }
}

public sealed class ProductionPlan
{
    public long Id { get; set; }
    public required string PlanNumber { get; set; }
    public long CustomerOrderId { get; set; }
    public CustomerOrder CustomerOrder { get; set; } = null!;
    public long MachineId { get; set; }
    public Machine Machine { get; set; } = null!;
    public int RequiredBatch { get; set; }
    public DateTime PlannedCompletionDate { get; set; }
    public ProductionPlanStatus Status { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<MaterialRequirement> MaterialRequirements { get; set; } = [];
    public ICollection<PurchaseRequest> PurchaseRequests { get; set; } = [];
}

public sealed class MaterialRequirement
{
    public long Id { get; set; }
    public long ProductionPlanId { get; set; }
    public ProductionPlan ProductionPlan { get; set; } = null!;
    public long RawMaterialId { get; set; }
    public RawMaterial RawMaterial { get; set; } = null!;
    public decimal RequiredQuantity { get; set; }
    public DateTime CalculatedAt { get; set; }
}

public sealed class PurchaseRequest
{
    public long Id { get; set; }
    public required string RequestNumber { get; set; }
    public long SourceProductionPlanId { get; set; }
    public ProductionPlan SourceProductionPlan { get; set; } = null!;
    public PurchaseRequestStatus Status { get; set; }
    public long RequestedByUserId { get; set; }
    public DateTime RequestedDate { get; set; }
    public long? ApprovedByUserId { get; set; }
    public DateTime? ApprovedDate { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<PurchaseRequestItem> Items { get; set; } = [];
    public IncomingPurchaseOrder? IncomingPurchaseOrder { get; set; }
}

public sealed class PurchaseRequestItem
{
    public long Id { get; set; }
    public long PurchaseRequestId { get; set; }
    public PurchaseRequest PurchaseRequest { get; set; } = null!;
    public long RawMaterialId { get; set; }
    public RawMaterial RawMaterial { get; set; } = null!;
    public decimal RequestedQuantity { get; set; }
    public DateTime ExpectedDate { get; set; }
}

public sealed class IncomingPurchaseOrder
{
    public long Id { get; set; }
    public required string PurchaseOrderNumber { get; set; }
    public long PurchaseRequestId { get; set; }
    public PurchaseRequest PurchaseRequest { get; set; } = null!;
    public DateTime ExpectedDate { get; set; }
    public DateTime? ReceivedDate { get; set; }
    public IncomingPurchaseOrderStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<IncomingPurchaseOrderItem> Items { get; set; } = [];
}

public sealed class IncomingPurchaseOrderItem
{
    public long Id { get; set; }
    public long IncomingPurchaseOrderId { get; set; }
    public IncomingPurchaseOrder IncomingPurchaseOrder { get; set; } = null!;
    public long RawMaterialId { get; set; }
    public RawMaterial RawMaterial { get; set; } = null!;
    public decimal OrderedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
}

public sealed class Machine
{
    public long Id { get; set; }
    public required string MachineCode { get; set; }
    public required string MachineName { get; set; }
    public MachineRunningStatus RunningStatus { get; set; }
    public decimal Temperature { get; set; }
    public decimal Speed { get; set; }
    public RiskStatus AlertStatus { get; set; }
    public DateTime LastUpdated { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class Alert
{
    public long Id { get; set; }
    public AlertType AlertType { get; set; }
    public AlertSeverity Severity { get; set; }
    public required string EntityName { get; set; }
    public long EntityId { get; set; }
    public required string Message { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public sealed class AuditLog
{
    public long Id { get; set; }
    public long? UserId { get; set; }
    public required string Username { get; set; }
    public required string Action { get; set; }
    public required string EntityName { get; set; }
    public long? EntityId { get; set; }
    public required string Result { get; set; }
    public required string RequestId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AiToolExecutionLog
{
    public long Id { get; set; }
    public long? UserId { get; set; }
    public required string ToolName { get; set; }
    public required string RequestId { get; set; }
    public int RecordCount { get; set; }
    public long DurationMs { get; set; }
    public required string Result { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
}
