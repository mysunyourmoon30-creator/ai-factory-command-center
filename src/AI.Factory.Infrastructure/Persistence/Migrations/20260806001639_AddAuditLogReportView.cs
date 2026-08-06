using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.Factory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogReportView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // See AddReportViews.Up for why CREATE VIEW is wrapped in EXEC(N'...') here.
            migrationBuilder.Sql("""
                EXEC(N'
                CREATE VIEW vw_AuditLogReport AS
                SELECT
                    a.Id,
                    a.UserId,
                    a.Username,
                    a.Action,
                    a.EntityName,
                    a.EntityId,
                    a.Result,
                    a.RequestId,
                    a.CreatedAt
                FROM AuditLogs a
                ')
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW vw_AuditLogReport");
        }
    }
}
