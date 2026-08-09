using AI.Factory.Core.Domain;
using AI.Factory.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AI.Factory.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<long>, long>(options)
{
    public DbSet<RawMaterial> RawMaterials => Set<RawMaterial>();
    public DbSet<Formulation> Formulations => Set<Formulation>();
    public DbSet<FormulationMaterial> FormulationMaterials => Set<FormulationMaterial>();
    public DbSet<CustomerOrder> CustomerOrders => Set<CustomerOrder>();
    public DbSet<ProductionPlan> ProductionPlans => Set<ProductionPlan>();
    public DbSet<MaterialRequirement> MaterialRequirements => Set<MaterialRequirement>();
    public DbSet<PurchaseRequest> PurchaseRequests => Set<PurchaseRequest>();
    public DbSet<PurchaseRequestItem> PurchaseRequestItems => Set<PurchaseRequestItem>();
    public DbSet<IncomingPurchaseOrder> IncomingPurchaseOrders => Set<IncomingPurchaseOrder>();
    public DbSet<IncomingPurchaseOrderItem> IncomingPurchaseOrderItems => Set<IncomingPurchaseOrderItem>();
    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AiToolExecutionLog> AiToolExecutionLogs => Set<AiToolExecutionLog>();

    public DbSet<ProductionRiskReportRow> ProductionRiskReport => Set<ProductionRiskReportRow>();
    public DbSet<PurchaseOrderStatusReportRow> PurchaseOrderStatusReport => Set<PurchaseOrderStatusReportRow>();
    public DbSet<MaterialShortageReportRow> MaterialShortageReport => Set<MaterialShortageReportRow>();
    public DbSet<AuditLogReportRow> AuditLogReport => Set<AuditLogReportRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureRawMaterial(modelBuilder.Entity<RawMaterial>());
        ConfigureFormulation(modelBuilder.Entity<Formulation>());
        ConfigureFormulationMaterial(modelBuilder.Entity<FormulationMaterial>());
        ConfigureCustomerOrder(modelBuilder.Entity<CustomerOrder>());
        ConfigureProductionPlan(modelBuilder.Entity<ProductionPlan>());
        ConfigureMaterialRequirement(modelBuilder.Entity<MaterialRequirement>());
        ConfigurePurchaseRequest(modelBuilder.Entity<PurchaseRequest>());
        ConfigurePurchaseRequestItem(modelBuilder.Entity<PurchaseRequestItem>());
        ConfigureIncomingPurchaseOrder(modelBuilder.Entity<IncomingPurchaseOrder>());
        ConfigureIncomingPurchaseOrderItem(modelBuilder.Entity<IncomingPurchaseOrderItem>());
        ConfigureMachine(modelBuilder.Entity<Machine>());
        ConfigureAlert(modelBuilder.Entity<Alert>());
        ConfigureAuditLog(modelBuilder.Entity<AuditLog>());
        ConfigureAiToolExecutionLog(modelBuilder.Entity<AiToolExecutionLog>());
        ConfigureReportViews(modelBuilder);
    }

    private static void ConfigureRawMaterial(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<RawMaterial> entity)
    {
        entity.ToTable("RawMaterials", t =>
        {
            t.HasCheckConstraint("CK_RawMaterials_CurrentStock", "[CurrentStock] >= 0");
            t.HasCheckConstraint("CK_RawMaterials_ReservedStock", "[ReservedStock] >= 0");
            t.HasCheckConstraint("CK_RawMaterials_LeadTimeDays", "[LeadTimeDays] >= 0");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Code).HasMaxLength(30).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
        entity.Property(x => x.Unit).HasMaxLength(30).IsRequired();
        entity.Property(x => x.CurrentStock).HasPrecision(18, 3);
        entity.Property(x => x.ReservedStock).HasPrecision(18, 3);
        entity.Property(x => x.CreatedAt).HasPrecision(0);
        entity.Property(x => x.UpdatedAt).HasPrecision(0);
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => x.Code).IsUnique();
    }

    private static void ConfigureFormulation(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Formulation> entity)
    {
        entity.ToTable("Formulations", t => t.HasCheckConstraint("CK_Formulations_BatchSize", "[BatchSize] > 0"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Code).HasMaxLength(30).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
        entity.Property(x => x.BatchSize).HasPrecision(18, 3);
        entity.Property(x => x.CreatedAt).HasPrecision(0);
        entity.Property(x => x.UpdatedAt).HasPrecision(0);
        entity.HasIndex(x => x.Code).IsUnique();
    }

    private static void ConfigureFormulationMaterial(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<FormulationMaterial> entity)
    {
        entity.ToTable("FormulationMaterials", t => t.HasCheckConstraint("CK_FormulationMaterials_WeightPerBatch", "[WeightPerBatch] > 0"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.WeightPerBatch).HasPrecision(18, 3);
        entity.HasIndex(x => new { x.FormulationId, x.RawMaterialId }).IsUnique();
        entity.HasOne(x => x.Formulation).WithMany(x => x.Materials).HasForeignKey(x => x.FormulationId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.RawMaterial).WithMany().HasForeignKey(x => x.RawMaterialId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureCustomerOrder(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<CustomerOrder> entity)
    {
        entity.ToTable("CustomerOrders", t => t.HasCheckConstraint("CK_CustomerOrders_Quantity", "[Quantity] > 0"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.OrderNumber).HasMaxLength(30).IsRequired();
        entity.Property(x => x.CustomerName).HasMaxLength(150).IsRequired();
        entity.Property(x => x.Quantity).HasPrecision(18, 3);
        entity.Property(x => x.Priority).HasMaxLength(30).IsRequired();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        entity.Property(x => x.DeliveryDate).HasPrecision(0);
        entity.Property(x => x.CreatedAt).HasPrecision(0);
        entity.Property(x => x.UpdatedAt).HasPrecision(0);
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => x.OrderNumber).IsUnique();
        entity.HasIndex(x => x.DeliveryDate);
        entity.HasIndex(x => x.Status);
        entity.HasOne(x => x.Formulation).WithMany().HasForeignKey(x => x.FormulationId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureProductionPlan(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<ProductionPlan> entity)
    {
        entity.ToTable("ProductionPlans", t => t.HasCheckConstraint("CK_ProductionPlans_RequiredBatch", "[RequiredBatch] > 0"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.PlanNumber).HasMaxLength(30).IsRequired();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        entity.Property(x => x.PlannedCompletionDate).HasPrecision(0);
        entity.Property(x => x.CreatedAt).HasPrecision(0);
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => x.PlanNumber).IsUnique();
        entity.HasIndex(x => x.CustomerOrderId).IsUnique();
        entity.HasIndex(x => x.PlannedCompletionDate);
        entity.HasIndex(x => x.Status);
        entity.HasOne(x => x.CustomerOrder).WithOne(x => x.ProductionPlan).HasForeignKey<ProductionPlan>(x => x.CustomerOrderId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Machine).WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureMaterialRequirement(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<MaterialRequirement> entity)
    {
        entity.ToTable("MaterialRequirements", t => t.HasCheckConstraint("CK_MaterialRequirements_RequiredQuantity", "[RequiredQuantity] > 0"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.RequiredQuantity).HasPrecision(18, 3);
        entity.Property(x => x.CalculatedAt).HasPrecision(0);
        entity.HasIndex(x => new { x.ProductionPlanId, x.RawMaterialId }).IsUnique();
        entity.HasIndex(x => x.RawMaterialId);
        entity.HasOne(x => x.ProductionPlan).WithMany(x => x.MaterialRequirements).HasForeignKey(x => x.ProductionPlanId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.RawMaterial).WithMany().HasForeignKey(x => x.RawMaterialId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigurePurchaseRequest(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<PurchaseRequest> entity)
    {
        entity.ToTable("PurchaseRequests");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.RequestNumber).HasMaxLength(30).IsRequired();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        entity.Property(x => x.RejectionReason).HasMaxLength(500);
        entity.Property(x => x.RequestedDate).HasPrecision(0);
        entity.Property(x => x.ApprovedDate).HasPrecision(0);
        entity.Property(x => x.CreatedAt).HasPrecision(0);
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => x.RequestNumber).IsUnique();
        entity.HasIndex(x => new { x.SourceProductionPlanId, x.Status });
        entity.HasOne(x => x.SourceProductionPlan).WithMany(x => x.PurchaseRequests).HasForeignKey(x => x.SourceProductionPlanId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigurePurchaseRequestItem(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<PurchaseRequestItem> entity)
    {
        entity.ToTable("PurchaseRequestItems", t => t.HasCheckConstraint("CK_PurchaseRequestItems_RequestedQuantity", "[RequestedQuantity] > 0"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.RequestedQuantity).HasPrecision(18, 3);
        entity.Property(x => x.ExpectedDate).HasPrecision(0);
        entity.HasIndex(x => new { x.PurchaseRequestId, x.RawMaterialId }).IsUnique();
        entity.HasIndex(x => new { x.RawMaterialId, x.PurchaseRequestId });
        entity.HasOne(x => x.PurchaseRequest).WithMany(x => x.Items).HasForeignKey(x => x.PurchaseRequestId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.RawMaterial).WithMany().HasForeignKey(x => x.RawMaterialId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureIncomingPurchaseOrder(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<IncomingPurchaseOrder> entity)
    {
        entity.ToTable("IncomingPurchaseOrders");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.PurchaseOrderNumber).HasMaxLength(30).IsRequired();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        entity.Property(x => x.ExpectedDate).HasPrecision(0);
        entity.Property(x => x.ReceivedDate).HasPrecision(0);
        entity.Property(x => x.CreatedAt).HasPrecision(0);
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => x.PurchaseOrderNumber).IsUnique();
        entity.HasIndex(x => x.PurchaseRequestId).IsUnique();
        entity.HasIndex(x => x.ExpectedDate);
        entity.HasIndex(x => x.Status);
        entity.HasOne(x => x.PurchaseRequest).WithOne(x => x.IncomingPurchaseOrder).HasForeignKey<IncomingPurchaseOrder>(x => x.PurchaseRequestId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureIncomingPurchaseOrderItem(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<IncomingPurchaseOrderItem> entity)
    {
        entity.ToTable("IncomingPurchaseOrderItems", t =>
        {
            t.HasCheckConstraint("CK_IncomingPOItems_OrderedQuantity", "[OrderedQuantity] > 0");
            t.HasCheckConstraint("CK_IncomingPOItems_ReceivedQuantity", "[ReceivedQuantity] >= 0 AND [ReceivedQuantity] <= [OrderedQuantity]");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.OrderedQuantity).HasPrecision(18, 3);
        entity.Property(x => x.ReceivedQuantity).HasPrecision(18, 3);
        entity.HasIndex(x => new { x.IncomingPurchaseOrderId, x.RawMaterialId }).IsUnique();
        // No explicit RawMaterialId index here on purpose: EF's convention already creates
        // IX_IncomingPurchaseOrderItems_RawMaterialId for the foreign key below, which the
        // shortage path's per-material lookups seek on. See finding A6.
        entity.HasOne(x => x.IncomingPurchaseOrder).WithMany(x => x.Items).HasForeignKey(x => x.IncomingPurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.RawMaterial).WithMany().HasForeignKey(x => x.RawMaterialId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureMachine(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Machine> entity)
    {
        entity.ToTable("Machines");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.MachineCode).HasMaxLength(30).IsRequired();
        entity.Property(x => x.MachineName).HasMaxLength(150).IsRequired();
        entity.Property(x => x.RunningStatus).HasConversion<string>().HasMaxLength(30);
        entity.Property(x => x.AlertStatus).HasConversion<string>().HasMaxLength(30);
        entity.Property(x => x.Temperature).HasPrecision(10, 2);
        entity.Property(x => x.Speed).HasPrecision(10, 2);
        entity.Property(x => x.LastUpdated).HasPrecision(0);
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => x.MachineCode).IsUnique();
    }

    private static void ConfigureAlert(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Alert> entity)
    {
        entity.ToTable("Alerts");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.AlertType).HasConversion<string>().HasMaxLength(30);
        entity.Property(x => x.Severity).HasConversion<string>().HasMaxLength(30);
        entity.Property(x => x.EntityName).HasMaxLength(150).IsRequired();
        entity.Property(x => x.Message).HasMaxLength(500).IsRequired();
        entity.Property(x => x.CreatedAt).HasPrecision(0);
        entity.Property(x => x.ResolvedAt).HasPrecision(0);
        entity.HasIndex(x => new { x.AlertType, x.EntityName, x.EntityId }).IsUnique().HasFilter("[IsActive] = 1");
        entity.HasIndex(x => new { x.Severity, x.IsActive, x.CreatedAt });
    }

    private static void ConfigureAuditLog(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AuditLog> entity)
    {
        entity.ToTable("AuditLogs");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Username).HasMaxLength(150).IsRequired();
        entity.Property(x => x.Action).HasMaxLength(150).IsRequired();
        entity.Property(x => x.EntityName).HasMaxLength(150).IsRequired();
        entity.Property(x => x.Result).HasMaxLength(500).IsRequired();
        entity.Property(x => x.RequestId).HasMaxLength(150).IsRequired();
        // 45 covers an IPv4-mapped IPv6 address, the longest form a RemoteIpAddress can take.
        entity.Property(x => x.IpAddress).HasMaxLength(45);
        entity.Property(x => x.UserAgent).HasMaxLength(512);
        entity.Property(x => x.CreatedAt).HasPrecision(0);
        entity.HasIndex(x => new { x.Username, x.EntityName, x.CreatedAt });
        // AuditLogs is append-only and grows without bound, so its read paths are the ones that
        // degrade first. The Username-leading index above cannot serve either of the two shapes
        // AuditLogService actually issues: an equality filter on Action alone, and a range
        // filter/sort on CreatedAt alone. Both were scans before these two indexes.
        entity.HasIndex(x => new { x.Action, x.CreatedAt });
        entity.HasIndex(x => x.CreatedAt);
    }

    private static void ConfigureAiToolExecutionLog(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AiToolExecutionLog> entity)
    {
        entity.ToTable("AiToolExecutionLogs");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.ToolName).HasMaxLength(150).IsRequired();
        entity.Property(x => x.RequestId).HasMaxLength(150).IsRequired();
        entity.Property(x => x.Result).HasMaxLength(500).IsRequired();
        entity.Property(x => x.ErrorMessage).HasMaxLength(500);
        entity.Property(x => x.CreatedAt).HasPrecision(0);
        entity.HasIndex(x => new { x.ToolName, x.CreatedAt });
    }

    /// <summary>
    /// Keyless entities backed by the locked report views (Master Scope V4 §14.7). Mapping via
    /// ToView (not ToTable) means GetTableName() returns null for these, so they never count
    /// toward the locked 14-application-table assertion.
    /// </summary>
    private static void ConfigureReportViews(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProductionRiskReportRow>(entity =>
        {
            entity.HasNoKey();
            entity.ToView("vw_ProductionRiskReport");
            entity.Property(x => x.PlanStatus).HasConversion<string>();
        });
        modelBuilder.Entity<PurchaseOrderStatusReportRow>(entity =>
        {
            entity.HasNoKey();
            entity.ToView("vw_PurchaseOrderStatusReport");
            entity.Property(x => x.Status).HasConversion<string>();
        });
        modelBuilder.Entity<MaterialShortageReportRow>(entity =>
        {
            entity.HasNoKey();
            entity.ToView("vw_MaterialShortageReport");
            entity.Property(x => x.PlanStatus).HasConversion<string>();
        });
        modelBuilder.Entity<AuditLogReportRow>(entity =>
        {
            entity.HasNoKey();
            entity.ToView("vw_AuditLogReport");
        });
    }
}
