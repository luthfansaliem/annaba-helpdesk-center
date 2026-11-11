using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Bookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bookable_resources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    capacity = table.Column<int>(type: "integer", nullable: true),
                    features = table.Column<List<string>>(type: "text[]", nullable: false),
                    open_from = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    open_until = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    open_days = table.Column<int>(type: "integer", nullable: false),
                    buffer_minutes = table.Column<int>(type: "integer", nullable: false),
                    max_minutes = table.Column<int>(type: "integer", nullable: true),
                    needs_approval = table.Column<bool>(type: "boolean", nullable: false),
                    allowed_role_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookable_resources", x => x.id);
                    table.CheckConstraint("ck_bookable_resources_buffer", "buffer_minutes BETWEEN 0 AND 240");
                    table.CheckConstraint("ck_bookable_resources_days", "open_days BETWEEN 1 AND 127");
                    table.CheckConstraint("ck_bookable_resources_hours", "(open_from IS NULL) = (open_until IS NULL) AND (open_from IS NULL OR open_until > open_from)");
                    table.ForeignKey(
                        name: "fk_bookable_resources_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bookable_resources_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    booked_for_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    blocked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attendees = table.Column<int>(type: "integer", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookings", x => x.id);
                    table.CheckConstraint("ck_bookings_range", "ends_at > starts_at AND blocked_until >= ends_at");
                    table.ForeignKey(
                        name: "fk_bookings_bookable_resources_resource_id",
                        column: x => x.resource_id,
                        principalTable: "bookable_resources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bookings_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bookable_resources_location_id",
                table: "bookable_resources",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookable_resources_tenant_id_code",
                table: "bookable_resources",
                columns: new[] { "tenant_id", "code" },
                unique: true,
                filter: "code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bookable_resources_tenant_id_kind_name",
                table: "bookable_resources",
                columns: new[] { "tenant_id", "kind", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_resource_id",
                table: "bookings",
                column: "resource_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_tenant_id_booked_for_id_starts_at",
                table: "bookings",
                columns: new[] { "tenant_id", "booked_for_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_tenant_id_created_by_id_starts_at",
                table: "bookings",
                columns: new[] { "tenant_id", "created_by_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_tenant_id_resource_id_starts_at",
                table: "bookings",
                columns: new[] { "tenant_id", "resource_id", "starts_at" });

            // BKG-002: two live bookings of one resource may not overlap, buffer included. The database holds this even when two requests race.
            // btree_gist lets the GiST index compare the uuid columns with "="; it is a trusted extension, so the database owner may create it.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            migrationBuilder.Sql("ALTER TABLE bookings ADD CONSTRAINT ex_bookings_no_overlap EXCLUDE USING gist (tenant_id WITH =, resource_id WITH =, tstzrange(starts_at, blocked_until, '[)') WITH &&) WHERE (status = 'Confirmed');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bookings");

            migrationBuilder.DropTable(
                name: "bookable_resources");
        }
    }
}
