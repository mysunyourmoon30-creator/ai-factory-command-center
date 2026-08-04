namespace AI.Factory.Core.Domain;

public enum CustomerOrderStatus { Draft, Planned, InProduction, Completed }
public enum ProductionPlanStatus { Planned, InProduction, Completed }
public enum PurchaseRequestStatus { Draft, PendingApproval, Approved, Rejected }
public enum IncomingPurchaseOrderStatus { Open, Partial, Received }
public enum MachineRunningStatus { Running, Stopped }
public enum RiskStatus { Normal, Warning, Critical }
public enum AlertSeverity { Warning, Critical }
public enum AlertType
{
    MaterialShortage,
    LateProduction,
    LatePurchaseOrder,
    MachineTemperature,
    MachineStopped
}
