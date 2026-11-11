using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Mail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mail_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    other_party = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    carrier = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    waybill = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    logged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    logged_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reminded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    done_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    done_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    collected_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    anonymised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mail_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_mail_items_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mail_items_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mail_items_location_id",
                table: "mail_items",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_mail_items_tenant_id_person_id_logged_at",
                table: "mail_items",
                columns: new[] { "tenant_id", "person_id", "logged_at" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_items_tenant_id_status_logged_at",
                table: "mail_items",
                columns: new[] { "tenant_id", "status", "logged_at" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_items_tenant_id_waybill",
                table: "mail_items",
                columns: new[] { "tenant_id", "waybill" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mail_items");
        }
    }
}
