using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.Factory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogClientContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "AuditLogs",
                type: "nvarchar(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserAgent",
                table: "AuditLogs",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            // EF does not track views, so it generated the two columns above and stopped.
            // vw_AuditLogReport has to be rebuilt by hand or AuditLogReportRow selects columns the
            // view does not expose and the CSV export fails at runtime.
            // CREATE VIEW must be the first statement in its batch, hence the EXEC(N'...')
            // wrapping - see AddReportViews.Up.
            migrationBuilder.Sql("DROP VIEW vw_AuditLogReport");
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
                    a.IpAddress,
                    a.UserAgent,
                    a.CreatedAt
                FROM AuditLogs a
                ')
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to the pre-migration shape first: the view has to stop referencing the columns
            // before they can be dropped.
            migrationBuilder.Sql("DROP VIEW vw_AuditLogReport");
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

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "UserAgent",
                table: "AuditLogs");
        }
    }
}
