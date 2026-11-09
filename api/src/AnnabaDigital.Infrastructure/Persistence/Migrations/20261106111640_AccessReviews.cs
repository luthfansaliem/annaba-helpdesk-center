using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccessReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "access_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_reviews", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "access_review_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reviewer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_review_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_access_review_items_access_reviews_review_id",
                        column: x => x.review_id,
                        principalTable: "access_reviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_access_review_items_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_access_review_items_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_access_review_items_review_id_decision",
                table: "access_review_items",
                columns: new[] { "review_id", "decision" });

            migrationBuilder.CreateIndex(
                name: "ix_access_review_items_review_id_user_id_role_id",
                table: "access_review_items",
                columns: new[] { "review_id", "user_id", "role_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_access_review_items_role_id",
                table: "access_review_items",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_review_items_user_id",
                table: "access_review_items",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_reviews_status",
                table: "access_reviews",
                column: "status",
                unique: true,
                filter: "status = 'Open'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_review_items");

            migrationBuilder.DropTable(
                name: "access_reviews");
        }
    }
}
