using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "resolve_with_work_orders",
                table: "tickets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "stage",
                table: "attachments",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "work_order_id",
                table: "attachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "checklist_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    items_json = table.Column<string>(type: "jsonb", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checklist_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_checklist_templates_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    instructions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    assignee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    required = table.Column<bool>(type: "boolean", nullable: false),
                    require_after_photo = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    hold_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completion_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_orders", x => x.id);
                    table.CheckConstraint("ck_work_orders_seq", "seq >= 1");
                    table.ForeignKey(
                        name: "fk_work_orders_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_orders_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_work_orders_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_orders_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_work_orders_users_assignee_id",
                        column: x => x.assignee_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "work_order_costs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_order_costs", x => x.id);
                    table.CheckConstraint("ck_work_order_costs_amounts", "quantity > 0 AND unit_cost >= 0");
                    table.ForeignKey(
                        name: "fk_work_order_costs_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_costs_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_order_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    required = table.Column<bool>(type: "boolean", nullable: false),
                    done_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    done_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    done_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_order_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_work_order_tasks_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_tasks_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_order_times",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    minutes = table.Column<int>(type: "integer", nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_order_times", x => x.id);
                    table.CheckConstraint("ck_work_order_times_minutes", "minutes BETWEEN 1 AND 1440");
                    table.ForeignKey(
                        name: "fk_work_order_times_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_times_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_work_order_id",
                table: "attachments",
                column: "work_order_id",
                filter: "work_order_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_checklist_templates_tenant_id_name",
                table: "checklist_templates",
                columns: new[] { "tenant_id", "name" },
                unique: true,
                filter: "archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_costs_tenant_id",
                table: "work_order_costs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_costs_work_order_id",
                table: "work_order_costs",
                column: "work_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_tasks_tenant_id",
                table: "work_order_tasks",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_tasks_work_order_id_position",
                table: "work_order_tasks",
                columns: new[] { "work_order_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_work_order_times_tenant_id_user_id_work_date",
                table: "work_order_times",
                columns: new[] { "tenant_id", "user_id", "work_date" });

            migrationBuilder.CreateIndex(
                name: "ix_work_order_times_work_order_id",
                table: "work_order_times",
                column: "work_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_asset_id",
                table: "work_orders",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_assignee_id",
                table: "work_orders",
                column: "assignee_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_team_id",
                table: "work_orders",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_tenant_id_assignee_id_status",
                table: "work_orders",
                columns: new[] { "tenant_id", "assignee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_tenant_id_status_updated_at",
                table: "work_orders",
                columns: new[] { "tenant_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_ticket_id_seq",
                table: "work_orders",
                columns: new[] { "ticket_id", "seq" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_attachments_work_orders_work_order_id",
                table: "attachments",
                column: "work_order_id",
                principalTable: "work_orders",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_attachments_work_orders_work_order_id",
                table: "attachments");

            migrationBuilder.DropTable(
                name: "checklist_templates");

            migrationBuilder.DropTable(
                name: "work_order_costs");

            migrationBuilder.DropTable(
                name: "work_order_tasks");

            migrationBuilder.DropTable(
                name: "work_order_times");

            migrationBuilder.DropTable(
                name: "work_orders");

            migrationBuilder.DropIndex(
                name: "ix_attachments_work_order_id",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "resolve_with_work_orders",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "stage",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "work_order_id",
                table: "attachments");
        }
    }
}
