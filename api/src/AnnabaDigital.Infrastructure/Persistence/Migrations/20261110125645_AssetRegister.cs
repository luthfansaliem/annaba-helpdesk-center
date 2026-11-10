using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssetRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "asset_id",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "asset_counters",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_counters", x => x.tenant_id);
                    table.CheckConstraint("ck_asset_counters_last", "last >= 0");
                    table.ForeignKey(
                        name: "fk_asset_counters_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "asset_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    fields_json = table.Column<string>(type: "jsonb", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_types", x => x.id);
                    table.ForeignKey(
                        name: "fk_asset_types_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    asset_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_to_id = table.Column<Guid>(type: "uuid", nullable: true),
                    department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    purchase_date = table.Column<DateOnly>(type: "date", nullable: true),
                    purchase_cost = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: true),
                    warranty_end = table.Column<DateOnly>(type: "date", nullable: true),
                    vendor_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    fields_json = table.Column<string>(type: "jsonb", nullable: false),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retire_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assets", x => x.id);
                    table.CheckConstraint("ck_assets_cost", "purchase_cost IS NULL OR purchase_cost >= 0");
                    table.ForeignKey(
                        name: "fk_assets_asset_types_asset_type_id",
                        column: x => x.asset_type_id,
                        principalTable: "asset_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assets_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assets_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assets_users_assigned_to_id",
                        column: x => x.assigned_to_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "asset_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_asset_events_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_asset_events_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_asset_events_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_asset_id",
                table: "tickets",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_tenant_id_asset_id",
                table: "tickets",
                columns: new[] { "tenant_id", "asset_id" },
                filter: "asset_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_asset_events_actor_id",
                table: "asset_events",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_asset_events_asset_id_created_at",
                table: "asset_events",
                columns: new[] { "asset_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_asset_events_tenant_id",
                table: "asset_events",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_asset_types_tenant_id_name",
                table: "asset_types",
                columns: new[] { "tenant_id", "name" },
                unique: true,
                filter: "archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_assets_asset_type_id",
                table: "assets",
                column: "asset_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_assets_assigned_to_id",
                table: "assets",
                column: "assigned_to_id");

            migrationBuilder.CreateIndex(
                name: "ix_assets_location_id",
                table: "assets",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_assets_tenant_id_asset_type_id",
                table: "assets",
                columns: new[] { "tenant_id", "asset_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assets_tenant_id_assigned_to_id",
                table: "assets",
                columns: new[] { "tenant_id", "assigned_to_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assets_tenant_id_location_id",
                table: "assets",
                columns: new[] { "tenant_id", "location_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assets_tenant_id_tag",
                table: "assets",
                columns: new[] { "tenant_id", "tag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assets_tenant_id_warranty_end",
                table: "assets",
                columns: new[] { "tenant_id", "warranty_end" },
                filter: "retired_at IS NULL AND warranty_end IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_tickets_assets_asset_id",
                table: "tickets",
                column: "asset_id",
                principalTable: "assets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tickets_assets_asset_id",
                table: "tickets");

            migrationBuilder.DropTable(
                name: "asset_counters");

            migrationBuilder.DropTable(
                name: "asset_events");

            migrationBuilder.DropTable(
                name: "assets");

            migrationBuilder.DropTable(
                name: "asset_types");

            migrationBuilder.DropIndex(
                name: "ix_tickets_asset_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "ix_tickets_tenant_id_asset_id",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "asset_id",
                table: "tickets");
        }
    }
}
