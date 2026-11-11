using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Visitors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "visitor_blocks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    company = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visitor_blocks", x => x.id);
                    table.ForeignKey(
                        name: "fk_visitor_blocks_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "visitor_settings",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acknowledgement = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ask_id_last4 = table.Column<bool>(type: "boolean", nullable: false),
                    retention_days = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visitor_settings", x => x.tenant_id);
                    table.ForeignKey(
                        name: "fk_visitor_settings_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "visits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visitor_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    company = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    vehicle_plate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    purpose = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    expected_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expected_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    flag = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    badge_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    id_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    checked_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    checked_in_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    checked_out_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    checked_out_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anonymised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visits", x => x.id);
                    table.CheckConstraint("ck_visits_window", "expected_until > expected_from");
                    table.ForeignKey(
                        name: "fk_visits_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_visits_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_visitor_blocks_tenant_id",
                table: "visitor_blocks",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_visits_location_id",
                table: "visits",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_visits_status_expected_until",
                table: "visits",
                columns: new[] { "status", "expected_until" });

            migrationBuilder.CreateIndex(
                name: "ix_visits_tenant_id_badge_number",
                table: "visits",
                columns: new[] { "tenant_id", "badge_number" },
                unique: true,
                filter: "status = 'CheckedIn'");

            migrationBuilder.CreateIndex(
                name: "ix_visits_tenant_id_expected_from",
                table: "visits",
                columns: new[] { "tenant_id", "expected_from" });

            migrationBuilder.CreateIndex(
                name: "ix_visits_tenant_id_host_id_expected_from",
                table: "visits",
                columns: new[] { "tenant_id", "host_id", "expected_from" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "visitor_blocks");

            migrationBuilder.DropTable(
                name: "visitor_settings");

            migrationBuilder.DropTable(
                name: "visits");
        }
    }
}
