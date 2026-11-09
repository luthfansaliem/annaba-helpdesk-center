using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountClosureRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_closure_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    execute_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_closure_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_closure_requests_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_account_closure_requests_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_closure_requests_execute_after",
                table: "account_closure_requests",
                column: "execute_after",
                filter: "status = 'Approved'");

            migrationBuilder.CreateIndex(
                name: "ix_account_closure_requests_tenant_id",
                table: "account_closure_requests",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_closure_requests_user_id",
                table: "account_closure_requests",
                column: "user_id",
                unique: true,
                filter: "status IN ('Pending', 'Approved')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_closure_requests");
        }
    }
}
