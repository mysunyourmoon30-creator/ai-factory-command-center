using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.Factory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE VIEW vw_ProductionRiskReport AS
                SELECT
                    pp.Id AS ProductionPlanId,
                    pp.PlanNumber,
                    co.OrderNumber,
                    f.Code AS FormulationCode,
                    m.MachineCode,
                    pp.RequiredBatch,
                    pp.PlannedCompletionDate,
                    pp.Status AS PlanStatus,
                    co.DeliveryDate
                FROM ProductionPlans pp
                JOIN CustomerOrders co ON co.Id = pp.CustomerOrderId
                JOIN Formulations f ON f.Id = co.FormulationId
                JOIN Machines m ON m.Id = pp.MachineId
                """);

            migrationBuilder.Sql("""
                CREATE VIEW vw_PurchaseOrderStatusReport AS
                SELECT
                    po.Id AS IncomingPurchaseOrderId,
                    po.PurchaseOrderNumber,
                    pr.RequestNumber,
                    po.ExpectedDate,
                    po.ReceivedDate,
                    po.Status,
                    SUM(i.OrderedQuantity) AS TotalOrderedQuantity,
                    SUM(i.ReceivedQuantity) AS TotalReceivedQuantity
                FROM IncomingPurchaseOrders po
                JOIN PurchaseRequests pr ON pr.Id = po.PurchaseRequestId
                JOIN IncomingPurchaseOrderItems i ON i.IncomingPurchaseOrderId = po.Id
                GROUP BY po.Id, po.PurchaseOrderNumber, pr.RequestNumber, po.ExpectedDate, po.ReceivedDate, po.Status
                """);

            migrationBuilder.Sql("""
                CREATE VIEW vw_MaterialShortageReport AS
                SELECT
                    pp.Id AS ProductionPlanId,
                    rm.Code AS RawMaterialCode,
                    rm.Name AS RawMaterialName,
                    rm.Unit,
                    rm.CurrentStock,
                    rm.ReservedStock,
                    pp.PlanNumber,
                    mr.RequiredQuantity,
                    pp.PlannedCompletionDate AS RequiredDate,
                    pp.Status AS PlanStatus
                FROM MaterialRequirements mr
                JOIN RawMaterials rm ON rm.Id = mr.RawMaterialId
                JOIN ProductionPlans pp ON pp.Id = mr.ProductionPlanId
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW vw_MaterialShortageReport");
            migrationBuilder.Sql("DROP VIEW vw_PurchaseOrderStatusReport");
            migrationBuilder.Sql("DROP VIEW vw_ProductionRiskReport");
        }
    }
}
