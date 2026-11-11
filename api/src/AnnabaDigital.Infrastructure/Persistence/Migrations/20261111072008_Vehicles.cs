using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Vehicles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "driver_licences",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    @class = table.Column<string>(name: "class", type: "character varying(10)", maxLength: 10, nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: false),
                    reminded = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_driver_licences", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_driver_licences_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_driver_licences_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "resource_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reminded = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resource_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_resource_documents_bookable_resources_resource_id",
                        column: x => x.resource_id,
                        principalTable: "bookable_resources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_resource_documents_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vehicle_trips",
                columns: table => new
                {
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    driver_id = table.Column<Guid>(type: "uuid", nullable: true),
                    needs_driver = table.Column<bool>(type: "boolean", nullable: false),
                    destination = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    purpose = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    odometer_out = table.Column<int>(type: "integer", nullable: true),
                    picked_up_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    picked_up_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    odometer_in = table.Column<int>(type: "integer", nullable: true),
                    returned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    returned_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fuel_cost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    toll_cost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    parking_cost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    damage_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    checklist_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicle_trips", x => x.booking_id);
                    table.CheckConstraint("ck_vehicle_trips_costs", "coalesce(fuel_cost, 0) >= 0 AND coalesce(toll_cost, 0) >= 0 AND coalesce(parking_cost, 0) >= 0");
                    table.CheckConstraint("ck_vehicle_trips_odometer", "odometer_in IS NULL OR (odometer_out IS NOT NULL AND odometer_in >= odometer_out)");
                    table.ForeignKey(
                        name: "fk_vehicle_trips_bookable_resources_resource_id",
                        column: x => x.resource_id,
                        principalTable: "bookable_resources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vehicle_trips_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_vehicle_trips_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_driver_licences_expires_on",
                table: "driver_licences",
                column: "expires_on");

            migrationBuilder.CreateIndex(
                name: "ix_driver_licences_tenant_id",
                table: "driver_licences",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_resource_documents_expires_on",
                table: "resource_documents",
                column: "expires_on");

            migrationBuilder.CreateIndex(
                name: "ix_resource_documents_resource_id",
                table: "resource_documents",
                column: "resource_id");

            migrationBuilder.CreateIndex(
                name: "ix_resource_documents_tenant_id_resource_id",
                table: "resource_documents",
                columns: new[] { "tenant_id", "resource_id" });

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_trips_resource_id",
                table: "vehicle_trips",
                column: "resource_id");

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_trips_tenant_id_driver_id",
                table: "vehicle_trips",
                columns: new[] { "tenant_id", "driver_id" });

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_trips_tenant_id_resource_id_returned_at",
                table: "vehicle_trips",
                columns: new[] { "tenant_id", "resource_id", "returned_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "driver_licences");

            migrationBuilder.DropTable(
                name: "resource_documents");

            migrationBuilder.DropTable(
                name: "vehicle_trips");
        }
    }
}
