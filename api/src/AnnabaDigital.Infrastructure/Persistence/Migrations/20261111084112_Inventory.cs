using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reorder_level = table.Column<int>(type: "integer", nullable: false),
                    reorder_quantity = table.Column<int>(type: "integer", nullable: true),
                    unit_cost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_items", x => x.id);
                    table.CheckConstraint("ck_inventory_items_levels", "reorder_level >= 0 AND (reorder_quantity IS NULL OR reorder_quantity > 0) AND (unit_cost IS NULL OR unit_cost >= 0)");
                    table.ForeignKey(
                        name: "fk_inventory_items_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_takes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    started_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_takes", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_takes_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_balances",
                columns: table => new
                {
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    low_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_balances", x => new { x.item_id, x.location_id });
                    table.CheckConstraint("ck_stock_balances_not_negative", "quantity >= 0");
                    table.ForeignKey(
                        name: "fk_stock_balances_inventory_items_item_id",
                        column: x => x.item_id,
                        principalTable: "inventory_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_stock_balances_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_balances_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    balance_after = table.Column<int>(type: "integer", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    reference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movements", x => x.id);
                    table.CheckConstraint("ck_stock_movements_quantity", "quantity <> 0 AND balance_after >= 0");
                    table.ForeignKey(
                        name: "fk_stock_movements_inventory_items_item_id",
                        column: x => x.item_id,
                        principalTable: "inventory_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_take_lines",
                columns: table => new
                {
                    stock_take_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected = table.Column<int>(type: "integer", nullable: false),
                    counted = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_take_lines", x => new { x.stock_take_id, x.item_id });
                    table.CheckConstraint("ck_stock_take_lines_counted", "counted IS NULL OR counted >= 0");
                    table.ForeignKey(
                        name: "fk_stock_take_lines_inventory_items_item_id",
                        column: x => x.item_id,
                        principalTable: "inventory_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_take_lines_stock_takes_stock_take_id",
                        column: x => x.stock_take_id,
                        principalTable: "stock_takes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_stock_take_lines_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_items_tenant_id_category_name",
                table: "inventory_items",
                columns: new[] { "tenant_id", "category", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_items_tenant_id_sku",
                table: "inventory_items",
                columns: new[] { "tenant_id", "sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_balances_location_id",
                table: "stock_balances",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_balances_tenant_id_location_id",
                table: "stock_balances",
                columns: new[] { "tenant_id", "location_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_group_id",
                table: "stock_movements",
                column: "group_id",
                filter: "group_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_item_id",
                table: "stock_movements",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_location_id",
                table: "stock_movements",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_tenant_id_item_id_at",
                table: "stock_movements",
                columns: new[] { "tenant_id", "item_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_tenant_id_kind_at",
                table: "stock_movements",
                columns: new[] { "tenant_id", "kind", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_tenant_id_location_id_at",
                table: "stock_movements",
                columns: new[] { "tenant_id", "location_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_take_lines_item_id",
                table: "stock_take_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_take_lines_tenant_id",
                table: "stock_take_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_takes_tenant_id_location_id",
                table: "stock_takes",
                columns: new[] { "tenant_id", "location_id" },
                unique: true,
                filter: "closed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_balances");

            migrationBuilder.DropTable(
                name: "stock_movements");

            migrationBuilder.DropTable(
                name: "stock_take_lines");

            migrationBuilder.DropTable(
                name: "inventory_items");

            migrationBuilder.DropTable(
                name: "stock_takes");
        }
    }
}
