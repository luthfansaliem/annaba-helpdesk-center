using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Directory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "employee_id",
                table: "user_profiles",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_id",
                table: "user_profiles",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "site_id",
                table: "user_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "directory_group_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_directory_group_mappings", x => x.id);
                    table.ForeignKey(
                        name: "fk_directory_group_mappings_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_directory_group_mappings_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "scim_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    member_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scim_groups", x => x.id);
                    table.ForeignKey(
                        name: "fk_scim_groups_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_profiles_external_id",
                table: "user_profiles",
                column: "external_id",
                filter: "external_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_profiles_site_id",
                table: "user_profiles",
                column: "site_id");

            migrationBuilder.CreateIndex(
                name: "ix_directory_group_mappings_role_id",
                table: "directory_group_mappings",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_directory_group_mappings_tenant_id_group_role_id",
                table: "directory_group_mappings",
                columns: new[] { "tenant_id", "group", "role_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scim_groups_member_ids",
                table: "scim_groups",
                column: "member_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_scim_groups_tenant_id_display_name",
                table: "scim_groups",
                columns: new[] { "tenant_id", "display_name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_user_profiles_locations_site_id",
                table: "user_profiles",
                column: "site_id",
                principalTable: "locations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_user_profiles_locations_site_id",
                table: "user_profiles");

            migrationBuilder.DropTable(
                name: "directory_group_mappings");

            migrationBuilder.DropTable(
                name: "scim_groups");

            migrationBuilder.DropIndex(
                name: "ix_user_profiles_external_id",
                table: "user_profiles");

            migrationBuilder.DropIndex(
                name: "ix_user_profiles_site_id",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "employee_id",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "external_id",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "site_id",
                table: "user_profiles");
        }
    }
}
