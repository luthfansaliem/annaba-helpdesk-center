using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantEventIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_security_events_tenant_id_ts",
                table: "security_events",
                columns: new[] { "tenant_id", "ts" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_tenant_id_ts",
                table: "audit_events",
                columns: new[] { "tenant_id", "ts" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_security_events_tenant_id_ts",
                table: "security_events");

            migrationBuilder.DropIndex(
                name: "ix_audit_events_tenant_id_ts",
                table: "audit_events");
        }
    }
}
