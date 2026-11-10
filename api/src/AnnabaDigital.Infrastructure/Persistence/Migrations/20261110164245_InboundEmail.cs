using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InboundEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbound_mailboxes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    default_service_id = table.Column<Guid>(type: "uuid", nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    token_hint = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbound_mailboxes", x => x.id);
                    table.ForeignKey(
                        name: "fk_inbound_mailboxes_services_default_service_id",
                        column: x => x.default_service_id,
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_inbound_mailboxes_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inbound_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mailbox_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    from_address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    outcome = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbound_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_inbound_messages_inbound_mailboxes_mailbox_id",
                        column: x => x.mailbox_id,
                        principalTable: "inbound_mailboxes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_inbound_messages_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inbound_messages_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_inbound_messages_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_mailboxes_default_service_id",
                table: "inbound_mailboxes",
                column: "default_service_id");

            migrationBuilder.CreateIndex(
                name: "ix_inbound_mailboxes_tenant_id",
                table: "inbound_mailboxes",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbound_mailboxes_token_hash",
                table: "inbound_mailboxes",
                column: "token_hash",
                unique: true,
                filter: "token_hash <> ''");

            migrationBuilder.CreateIndex(
                name: "ix_inbound_messages_mailbox_id_message_id",
                table: "inbound_messages",
                columns: new[] { "mailbox_id", "message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbound_messages_mailbox_id_received_at",
                table: "inbound_messages",
                columns: new[] { "mailbox_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_messages_mailbox_id_user_id_received_at",
                table: "inbound_messages",
                columns: new[] { "mailbox_id", "user_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_messages_tenant_id",
                table: "inbound_messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_inbound_messages_ticket_id",
                table: "inbound_messages",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_inbound_messages_user_id",
                table: "inbound_messages",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbound_messages");

            migrationBuilder.DropTable(
                name: "inbound_mailboxes");
        }
    }
}
