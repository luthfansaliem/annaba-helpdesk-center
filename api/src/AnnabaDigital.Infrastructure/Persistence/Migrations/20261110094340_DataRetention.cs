using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DataRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "anonymised_at",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "retention_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    finished_ticket_days = table.Column<int>(type: "integer", nullable: true),
                    ticket_action = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    attachment_days = table.Column<int>(type: "integer", nullable: true),
                    notification_days = table.Column<int>(type: "integer", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_run_json = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retention_policies", x => x.id);
                    table.CheckConstraint("ck_retention_policies_action", "ticket_action IN ('anonymise', 'delete')");
                    table.CheckConstraint("ck_retention_policies_periods", "(finished_ticket_days IS NULL OR finished_ticket_days BETWEEN 30 AND 3650) AND (attachment_days IS NULL OR attachment_days BETWEEN 7 AND 3650) AND (notification_days IS NULL OR notification_days BETWEEN 7 AND 3650)");
                    table.ForeignKey(
                        name: "fk_retention_policies_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticket_legal_holds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    placed_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    placed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_legal_holds", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticket_legal_holds_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticket_legal_holds_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ticket_legal_holds_users_placed_by_id",
                        column: x => x.placed_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_retention_policies_tenant_id",
                table: "retention_policies",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ticket_legal_holds_placed_by_id",
                table: "ticket_legal_holds",
                column: "placed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_legal_holds_tenant_id_placed_at",
                table: "ticket_legal_holds",
                columns: new[] { "tenant_id", "placed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_legal_holds_ticket_id",
                table: "ticket_legal_holds",
                column: "ticket_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "retention_policies");

            migrationBuilder.DropTable(
                name: "ticket_legal_holds");

            migrationBuilder.DropColumn(
                name: "anonymised_at",
                table: "tickets");
        }
    }
}
