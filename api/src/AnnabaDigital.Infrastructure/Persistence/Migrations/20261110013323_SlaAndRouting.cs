using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SlaAndRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "assigned_at",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "first_responded_at",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "first_response_due_at",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "resolution_due_at",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sla_breached_at",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "sla_calendar_id",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sla_escalate1percent",
                table: "tickets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "sla_escalate2percent",
                table: "tickets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "sla_escalation_level",
                table: "tickets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "sla_first_response_minutes",
                table: "tickets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sla_paused_at",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sla_paused_minutes",
                table: "tickets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "sla_policy_id",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sla_resolution_minutes",
                table: "tickets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sla_start_at",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sla_state",
                table: "tickets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None"); // existing tickets have no SLA

            migrationBuilder.CreateTable(
                name: "assignment_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    strategy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_assignment_rules_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assignment_rules_service_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "service_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assignment_rules_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assignment_rules_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assignment_rules_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sla_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    calendar_id = table.Column<Guid>(type: "uuid", nullable: true),
                    first_response_minutes = table.Column<int>(type: "integer", nullable: false),
                    resolution_minutes = table.Column<int>(type: "integer", nullable: false),
                    escalate1percent = table.Column<int>(type: "integer", nullable: false),
                    escalate2percent = table.Column<int>(type: "integer", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sla_policies", x => x.id);
                    table.CheckConstraint("ck_sla_policies_escalation", "escalate1percent BETWEEN 1 AND 98 AND escalate2percent > escalate1percent AND escalate2percent <= 99");
                    table.CheckConstraint("ck_sla_policies_targets", "first_response_minutes >= 1 AND resolution_minutes >= first_response_minutes");
                    table.ForeignKey(
                        name: "fk_sla_policies_business_calendars_calendar_id",
                        column: x => x.calendar_id,
                        principalTable: "business_calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sla_policies_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sla_policies_service_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "service_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sla_policies_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sla_policies_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_resolution_due_at",
                table: "tickets",
                column: "resolution_due_at",
                filter: "sla_policy_id IS NOT NULL AND resolved_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_sla_policy_id",
                table: "tickets",
                column: "sla_policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_tenant_id_sla_state",
                table: "tickets",
                columns: new[] { "tenant_id", "sla_state" });

            migrationBuilder.CreateIndex(
                name: "ix_assignment_rules_category_id",
                table: "assignment_rules",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignment_rules_location_id",
                table: "assignment_rules",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignment_rules_service_id",
                table: "assignment_rules",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignment_rules_team_id",
                table: "assignment_rules",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignment_rules_tenant_id_name",
                table: "assignment_rules",
                columns: new[] { "tenant_id", "name" },
                unique: true,
                filter: "archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_assignment_rules_tenant_id_position",
                table: "assignment_rules",
                columns: new[] { "tenant_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_sla_policies_calendar_id",
                table: "sla_policies",
                column: "calendar_id");

            migrationBuilder.CreateIndex(
                name: "ix_sla_policies_category_id",
                table: "sla_policies",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_sla_policies_location_id",
                table: "sla_policies",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_sla_policies_service_id",
                table: "sla_policies",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_sla_policies_tenant_id_name",
                table: "sla_policies",
                columns: new[] { "tenant_id", "name" },
                unique: true,
                filter: "archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sla_policies_tenant_id_position",
                table: "sla_policies",
                columns: new[] { "tenant_id", "position" });

            migrationBuilder.AddForeignKey(
                name: "fk_tickets_sla_policies_sla_policy_id",
                table: "tickets",
                column: "sla_policy_id",
                principalTable: "sla_policies",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tickets_sla_policies_sla_policy_id",
                table: "tickets");

            migrationBuilder.DropTable(
                name: "assignment_rules");

            migrationBuilder.DropTable(
                name: "sla_policies");

            migrationBuilder.DropIndex(
                name: "ix_tickets_resolution_due_at",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "ix_tickets_sla_policy_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "ix_tickets_tenant_id_sla_state",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "assigned_at",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "first_responded_at",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "first_response_due_at",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "resolution_due_at",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "sla_breached_at",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "sla_calendar_id",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "sla_escalate1percent",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "sla_escalate2percent",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "sla_escalation_level",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "sla_first_response_minutes",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "sla_paused_at",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "sla_paused_minutes",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "sla_policy_id",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "sla_resolution_minutes",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "sla_start_at",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "sla_state",
                table: "tickets");
        }
    }
}
