using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApprovalDelegation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "manager_id",
                table: "user_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "on_behalf_of_id",
                table: "ticket_approvals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "approval_delegations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delegate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_delegations", x => x.id);
                    table.CheckConstraint("ck_approval_delegations_period", "ends_at > starts_at");
                    table.CheckConstraint("ck_approval_delegations_self", "user_id <> delegate_id");
                    table.ForeignKey(
                        name: "fk_approval_delegations_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_approval_delegations_users_delegate_id",
                        column: x => x.delegate_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_approval_delegations_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_profiles_manager_id",
                table: "user_profiles",
                column: "manager_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_delegations_delegate_id",
                table: "approval_delegations",
                column: "delegate_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_delegations_tenant_id_delegate_id",
                table: "approval_delegations",
                columns: new[] { "tenant_id", "delegate_id" });

            migrationBuilder.CreateIndex(
                name: "ix_approval_delegations_tenant_id_user_id_starts_at",
                table: "approval_delegations",
                columns: new[] { "tenant_id", "user_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_approval_delegations_user_id",
                table: "approval_delegations",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_user_profiles_users_manager_id",
                table: "user_profiles",
                column: "manager_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_user_profiles_users_manager_id",
                table: "user_profiles");

            migrationBuilder.DropTable(
                name: "approval_delegations");

            migrationBuilder.DropIndex(
                name: "ix_user_profiles_manager_id",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "manager_id",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "on_behalf_of_id",
                table: "ticket_approvals");
        }
    }
}
