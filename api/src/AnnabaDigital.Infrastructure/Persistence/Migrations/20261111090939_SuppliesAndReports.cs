using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SuppliesAndReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "stock_item_id",
                table: "work_order_costs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "stock_location_id",
                table: "work_order_costs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "stock_quantity",
                table: "work_order_costs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "attachment_content",
                table: "outbox_emails",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attachment_name",
                table: "outbox_emails",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attachment_type",
                table: "outbox_emails",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "report_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    report = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    frequency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    weekday = table.Column<int>(type: "integer", nullable: false),
                    month_day = table.Column<int>(type: "integer", nullable: false),
                    hour = table.Column<int>(type: "integer", nullable: false),
                    days = table.Column<int>(type: "integer", nullable: false),
                    recipient_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_result = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_schedules", x => x.id);
                    table.CheckConstraint("ck_report_schedules_kinds", "format IN ('csv','xlsx') AND frequency IN ('daily','weekly','monthly')");
                    table.CheckConstraint("ck_report_schedules_when", "hour BETWEEN 0 AND 23 AND weekday BETWEEN 1 AND 7 AND month_day BETWEEN 1 AND 28 AND days BETWEEN 1 AND 365");
                    table.ForeignKey(
                        name: "fk_report_schedules_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_report_schedules_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "supply_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requester_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    estimated_cost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    approver_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    fulfilled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supply_requests", x => x.id);
                    table.CheckConstraint("ck_supply_requests_cost", "estimated_cost >= 0");
                    table.ForeignKey(
                        name: "fk_supply_requests_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supply_requests_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supply_requests_users_approver_id",
                        column: x => x.approver_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_supply_requests_users_requester_id",
                        column: x => x.requester_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supply_settings",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approval_above = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    default_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supply_settings", x => x.tenant_id);
                    table.ForeignKey(
                        name: "fk_supply_settings_locations_default_location_id",
                        column: x => x.default_location_id,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_supply_settings_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supply_request_lines",
                columns: table => new
                {
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    issued = table.Column<int>(type: "integer", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supply_request_lines", x => new { x.request_id, x.item_id });
                    table.CheckConstraint("ck_supply_request_lines_qty", "quantity > 0 AND issued >= 0 AND issued <= quantity");
                    table.ForeignKey(
                        name: "fk_supply_request_lines_inventory_items_item_id",
                        column: x => x.item_id,
                        principalTable: "inventory_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supply_request_lines_supply_requests_request_id",
                        column: x => x.request_id,
                        principalTable: "supply_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_supply_request_lines_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_work_order_costs_stock_item_id",
                table: "work_order_costs",
                column: "stock_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_costs_stock_location_id",
                table: "work_order_costs",
                column: "stock_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_report_schedules_next_run_at",
                table: "report_schedules",
                column: "next_run_at",
                filter: "active");

            migrationBuilder.CreateIndex(
                name: "ix_report_schedules_owner_id",
                table: "report_schedules",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_report_schedules_tenant_id_owner_id",
                table: "report_schedules",
                columns: new[] { "tenant_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supply_request_lines_item_id",
                table: "supply_request_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_supply_request_lines_tenant_id_item_id",
                table: "supply_request_lines",
                columns: new[] { "tenant_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supply_requests_approver_id",
                table: "supply_requests",
                column: "approver_id");

            migrationBuilder.CreateIndex(
                name: "ix_supply_requests_location_id",
                table: "supply_requests",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_supply_requests_requester_id",
                table: "supply_requests",
                column: "requester_id");

            migrationBuilder.CreateIndex(
                name: "ix_supply_requests_tenant_id_approver_id",
                table: "supply_requests",
                columns: new[] { "tenant_id", "approver_id" },
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_supply_requests_tenant_id_number",
                table: "supply_requests",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_supply_requests_tenant_id_requester_id_created_at",
                table: "supply_requests",
                columns: new[] { "tenant_id", "requester_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_supply_requests_tenant_id_status_created_at",
                table: "supply_requests",
                columns: new[] { "tenant_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_supply_settings_default_location_id",
                table: "supply_settings",
                column: "default_location_id");

            migrationBuilder.AddForeignKey(
                name: "fk_work_order_costs_inventory_items_stock_item_id",
                table: "work_order_costs",
                column: "stock_item_id",
                principalTable: "inventory_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_work_order_costs_locations_stock_location_id",
                table: "work_order_costs",
                column: "stock_location_id",
                principalTable: "locations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_work_order_costs_inventory_items_stock_item_id",
                table: "work_order_costs");

            migrationBuilder.DropForeignKey(
                name: "fk_work_order_costs_locations_stock_location_id",
                table: "work_order_costs");

            migrationBuilder.DropTable(
                name: "report_schedules");

            migrationBuilder.DropTable(
                name: "supply_request_lines");

            migrationBuilder.DropTable(
                name: "supply_settings");

            migrationBuilder.DropTable(
                name: "supply_requests");

            migrationBuilder.DropIndex(
                name: "ix_work_order_costs_stock_item_id",
                table: "work_order_costs");

            migrationBuilder.DropIndex(
                name: "ix_work_order_costs_stock_location_id",
                table: "work_order_costs");

            migrationBuilder.DropColumn(
                name: "stock_item_id",
                table: "work_order_costs");

            migrationBuilder.DropColumn(
                name: "stock_location_id",
                table: "work_order_costs");

            migrationBuilder.DropColumn(
                name: "stock_quantity",
                table: "work_order_costs");

            migrationBuilder.DropColumn(
                name: "attachment_content",
                table: "outbox_emails");

            migrationBuilder.DropColumn(
                name: "attachment_name",
                table: "outbox_emails");

            migrationBuilder.DropColumn(
                name: "attachment_type",
                table: "outbox_emails");
        }
    }
}
