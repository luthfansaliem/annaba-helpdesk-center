using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_roles_key",
                table: "roles");

            migrationBuilder.DropIndex(
                name: "ix_roles_name",
                table: "roles");

            migrationBuilder.DropIndex(
                name: "ix_access_reviews_status",
                table: "access_reviews");

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "users",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-7000-8000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "sessions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-7000-8000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "security_events",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-7000-8000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "roles",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-7000-8000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "invitations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-7000-8000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "audit_events",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-7000-8000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "access_reviews",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-7000-8000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "access_review_items",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-7000-8000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "access_requests",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-7000-8000-000000000001"));

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_platform = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                });

            // role names are case-insensitively unique within an organisation, not across all of them
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_roles_name_ci;");
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_roles_name_ci ON roles (tenant_id, lower(name)) WHERE archived_at IS NULL;");

            // Everything that exists today belongs to the default (platform) tenant; it must exist before the foreign keys that point at it.
            migrationBuilder.Sql("INSERT INTO tenants (id, slug, name, status, is_platform, created_at) VALUES ('00000000-0000-7000-8000-000000000001', 'default', 'Default organisation', 'Active', true, now()) ON CONFLICT DO NOTHING;");

            migrationBuilder.CreateIndex(
                name: "ix_users_tenant_id",
                table: "users",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_tenant_id",
                table: "sessions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_security_events_tenant_id",
                table: "security_events",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_tenant_id",
                table: "roles",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_tenant_id_key",
                table: "roles",
                columns: new[] { "tenant_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roles_tenant_id_name",
                table: "roles",
                columns: new[] { "tenant_id", "name" },
                unique: true,
                filter: "archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_invitations_tenant_id",
                table: "invitations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_tenant_id",
                table: "audit_events",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_reviews_tenant_id",
                table: "access_reviews",
                column: "tenant_id",
                unique: true,
                filter: "status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_access_review_items_tenant_id",
                table: "access_review_items",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_requests_tenant_id",
                table: "access_requests",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_slug",
                table: "tenants",
                column: "slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_access_requests_tenants_tenant_id",
                table: "access_requests",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_access_review_items_tenants_tenant_id",
                table: "access_review_items",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_access_reviews_tenants_tenant_id",
                table: "access_reviews",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_audit_events_tenants_tenant_id",
                table: "audit_events",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_invitations_tenants_tenant_id",
                table: "invitations",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_roles_tenants_tenant_id",
                table: "roles",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_security_events_tenants_tenant_id",
                table: "security_events",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sessions_tenants_tenant_id",
                table: "sessions",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_users_tenants_tenant_id",
                table: "users",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_roles_name_ci;");
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_roles_name_ci ON roles (lower(name)) WHERE archived_at IS NULL;");

            migrationBuilder.DropForeignKey(
                name: "fk_access_requests_tenants_tenant_id",
                table: "access_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_access_review_items_tenants_tenant_id",
                table: "access_review_items");

            migrationBuilder.DropForeignKey(
                name: "fk_access_reviews_tenants_tenant_id",
                table: "access_reviews");

            migrationBuilder.DropForeignKey(
                name: "fk_audit_events_tenants_tenant_id",
                table: "audit_events");

            migrationBuilder.DropForeignKey(
                name: "fk_invitations_tenants_tenant_id",
                table: "invitations");

            migrationBuilder.DropForeignKey(
                name: "fk_roles_tenants_tenant_id",
                table: "roles");

            migrationBuilder.DropForeignKey(
                name: "fk_security_events_tenants_tenant_id",
                table: "security_events");

            migrationBuilder.DropForeignKey(
                name: "fk_sessions_tenants_tenant_id",
                table: "sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_users_tenants_tenant_id",
                table: "users");

            migrationBuilder.DropTable(
                name: "tenants");

            migrationBuilder.DropIndex(
                name: "ix_users_tenant_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_sessions_tenant_id",
                table: "sessions");

            migrationBuilder.DropIndex(
                name: "ix_security_events_tenant_id",
                table: "security_events");

            migrationBuilder.DropIndex(
                name: "ix_roles_tenant_id",
                table: "roles");

            migrationBuilder.DropIndex(
                name: "ix_roles_tenant_id_key",
                table: "roles");

            migrationBuilder.DropIndex(
                name: "ix_roles_tenant_id_name",
                table: "roles");

            migrationBuilder.DropIndex(
                name: "ix_invitations_tenant_id",
                table: "invitations");

            migrationBuilder.DropIndex(
                name: "ix_audit_events_tenant_id",
                table: "audit_events");

            migrationBuilder.DropIndex(
                name: "ix_access_reviews_tenant_id",
                table: "access_reviews");

            migrationBuilder.DropIndex(
                name: "ix_access_review_items_tenant_id",
                table: "access_review_items");

            migrationBuilder.DropIndex(
                name: "ix_access_requests_tenant_id",
                table: "access_requests");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "security_events");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "invitations");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "access_reviews");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "access_review_items");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "access_requests");

            migrationBuilder.CreateIndex(
                name: "ix_roles_key",
                table: "roles",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roles_name",
                table: "roles",
                column: "name",
                unique: true,
                filter: "archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_access_reviews_status",
                table: "access_reviews",
                column: "status",
                unique: true,
                filter: "status = 'Open'");
        }
    }
}
