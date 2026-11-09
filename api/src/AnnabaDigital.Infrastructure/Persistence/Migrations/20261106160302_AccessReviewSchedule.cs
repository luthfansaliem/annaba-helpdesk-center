using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccessReviewSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "access_review_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    every_days = table.Column<int>(type: "integer", nullable: false),
                    due_in_days = table.Column<int>(type: "integer", nullable: false),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_review_schedules", x => x.id);
                    table.ForeignKey(
                        name: "fk_access_review_schedules_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_access_review_schedules_next_run_at",
                table: "access_review_schedules",
                column: "next_run_at",
                filter: "enabled");

            migrationBuilder.CreateIndex(
                name: "ix_access_review_schedules_tenant_id",
                table: "access_review_schedules",
                column: "tenant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_review_schedules");
        }
    }
}
