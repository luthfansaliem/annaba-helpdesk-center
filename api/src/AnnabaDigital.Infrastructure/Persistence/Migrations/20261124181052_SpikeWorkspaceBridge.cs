using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SpikeWorkspaceBridge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_email",
                table: "users");

            migrationBuilder.AddColumn<string>(
                name: "account_ref",
                table: "users",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "entitlement_revision",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "workspace_key",
                table: "tenants",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_tenant_id_account_ref",
                table: "users",
                columns: new[] { "tenant_id", "account_ref" },
                unique: true,
                filter: "account_ref is not null");

            migrationBuilder.CreateIndex(
                name: "ix_users_tenant_id_email",
                table: "users",
                columns: new[] { "tenant_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenants_workspace_key",
                table: "tenants",
                column: "workspace_key",
                unique: true,
                filter: "workspace_key is not null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_tenant_id_account_ref",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_tenant_id_email",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_tenants_workspace_key",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "account_ref",
                table: "users");

            migrationBuilder.DropColumn(
                name: "entitlement_revision",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "workspace_key",
                table: "tenants");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);
        }
    }
}
