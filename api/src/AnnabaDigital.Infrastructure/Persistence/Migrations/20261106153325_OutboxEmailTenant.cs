using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutboxEmailTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "outbox_emails",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-7000-8000-000000000001")); // existing mail belongs to the default tenant unless its recipient says otherwise (below)

            // Mail already queued or sent follows its recipient's organisation.
            migrationBuilder.Sql("""
                UPDATE outbox_emails o SET tenant_id = u.tenant_id
                FROM users u WHERE lower(u.email) = lower(o.to_address) AND u.tenant_id <> o.tenant_id;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_emails_tenant_id_created_at",
                table: "outbox_emails",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_outbox_emails_tenants_tenant_id",
                table: "outbox_emails",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_outbox_emails_tenants_tenant_id",
                table: "outbox_emails");

            migrationBuilder.DropIndex(
                name: "ix_outbox_emails_tenant_id_created_at",
                table: "outbox_emails");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "outbox_emails");
        }
    }
}
