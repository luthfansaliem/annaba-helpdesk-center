using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BookingLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "checked_in_at",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decided_at",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "decided_by_id",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decision_note",
                table: "bookings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reminded_at",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "repeat",
                table: "bookings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "series_id",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "check_in_minutes",
                table: "bookable_resources",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "booking_feeds",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_feeds", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_booking_feeds_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_booking_feeds_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_series_id",
                table: "bookings",
                column: "series_id",
                filter: "series_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_status_starts_at",
                table: "bookings",
                columns: new[] { "status", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_booking_feeds_tenant_id",
                table: "booking_feeds",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_booking_feeds_token_hash",
                table: "booking_feeds",
                column: "token_hash",
                unique: true);

            // a booking waiting for approval holds its slot too (BKG-005)
            migrationBuilder.Sql("ALTER TABLE bookings DROP CONSTRAINT ex_bookings_no_overlap;");
            migrationBuilder.Sql("ALTER TABLE bookings ADD CONSTRAINT ex_bookings_no_overlap EXCLUDE USING gist (tenant_id WITH =, resource_id WITH =, tstzrange(starts_at, blocked_until, '[)') WITH &&) WHERE (status IN ('Confirmed', 'Pending'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE bookings DROP CONSTRAINT ex_bookings_no_overlap;");
            migrationBuilder.Sql("UPDATE bookings SET status = 'Cancelled' WHERE status IN ('Pending', 'Rejected', 'Released');");
            migrationBuilder.Sql("ALTER TABLE bookings ADD CONSTRAINT ex_bookings_no_overlap EXCLUDE USING gist (tenant_id WITH =, resource_id WITH =, tstzrange(starts_at, blocked_until, '[)') WITH &&) WHERE (status = 'Confirmed');");
            migrationBuilder.DropTable(
                name: "booking_feeds");

            migrationBuilder.DropIndex(
                name: "ix_bookings_series_id",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "ix_bookings_status_starts_at",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "checked_in_at",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "decided_at",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "decided_by_id",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "decision_note",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "reminded_at",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "repeat",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "series_id",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "check_in_minutes",
                table: "bookable_resources");
        }
    }
}
