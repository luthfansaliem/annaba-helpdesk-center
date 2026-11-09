using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RolesHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "roles",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Role names are unique ignoring case among non-archived roles (the app checks too; this is the race-proof guarantee).
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_roles_name_ci ON roles (lower(name)) WHERE archived_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_roles_name_ci;");
            migrationBuilder.DropColumn(
                name: "version",
                table: "roles");
        }
    }
}
