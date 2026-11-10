using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Maintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "maintenance_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    instructions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    asset_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    every = table.Column<int>(type: "integer", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    lead_days = table.Column<int>(type: "integer", nullable: false),
                    next_index = table.Column<int>(type: "integer", nullable: false),
                    next_due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    checklist_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assignee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    require_after_photo = table.Column<bool>(type: "boolean", nullable: false),
                    paused_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_plans", x => x.id);
                    table.CheckConstraint("ck_maintenance_plans_dates", "ends_on IS NULL OR ends_on >= starts_on");
                    table.CheckConstraint("ck_maintenance_plans_every", "every >= 1");
                    table.CheckConstraint("ck_maintenance_plans_lead", "lead_days >= 0 AND lead_days <= 90");
                    table.CheckConstraint("ck_maintenance_plans_target", "asset_id IS NOT NULL OR asset_type_id IS NOT NULL OR location_id IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_maintenance_plans_asset_types_asset_type_id",
                        column: x => x.asset_type_id,
                        principalTable: "asset_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_maintenance_plans_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_maintenance_plans_checklist_templates_checklist_template_id",
                        column: x => x.checklist_template_id,
                        principalTable: "checklist_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_maintenance_plans_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_maintenance_plans_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_maintenance_plans_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_maintenance_plans_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_maintenance_plans_users_assignee_id",
                        column: x => x.assignee_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_maintenance_plans_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_maintenance_plans_vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_visits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_key = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    missed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_visits", x => x.id);
                    table.ForeignKey(
                        name: "fk_maintenance_visits_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_maintenance_visits_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_maintenance_visits_maintenance_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "maintenance_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_maintenance_visits_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_maintenance_visits_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_maintenance_visits_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plans_asset_id",
                table: "maintenance_plans",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plans_asset_type_id",
                table: "maintenance_plans",
                column: "asset_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plans_assignee_id",
                table: "maintenance_plans",
                column: "assignee_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plans_checklist_template_id",
                table: "maintenance_plans",
                column: "checklist_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plans_location_id",
                table: "maintenance_plans",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plans_next_due_on",
                table: "maintenance_plans",
                column: "next_due_on",
                filter: "paused_at IS NULL AND next_due_on IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plans_owner_id",
                table: "maintenance_plans",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plans_service_id",
                table: "maintenance_plans",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plans_team_id",
                table: "maintenance_plans",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plans_tenant_id_name",
                table: "maintenance_plans",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plans_vendor_id",
                table: "maintenance_plans",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_visits_asset_id",
                table: "maintenance_visits",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_visits_location_id",
                table: "maintenance_visits",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_visits_plan_id_target_key_due_on",
                table: "maintenance_visits",
                columns: new[] { "plan_id", "target_key", "due_on" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_visits_tenant_id_due_on",
                table: "maintenance_visits",
                columns: new[] { "tenant_id", "due_on" });

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_visits_ticket_id",
                table: "maintenance_visits",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_visits_work_order_id",
                table: "maintenance_visits",
                column: "work_order_id",
                filter: "work_order_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maintenance_visits");

            migrationBuilder.DropTable(
                name: "maintenance_plans");
        }
    }
}
