using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChannelsAndFieldMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "channel_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    purpose = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    notification_kind = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    replaces_email = table.Column<bool>(type: "boolean", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    fell_back_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channel_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "fk_channel_deliveries_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_channel_deliveries_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "channel_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    settings_json = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    secret_ciphertext = table.Column<byte[]>(type: "bytea", nullable: true),
                    secret_nonce = table.Column<byte[]>(type: "bytea", nullable: true),
                    secret_key_version = table.Column<int>(type: "integer", nullable: false),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channel_settings", x => x.id);
                    table.ForeignKey(
                        name: "fk_channel_settings_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "offline_ops",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offline_ops", x => new { x.user_id, x.key });
                    table.ForeignKey(
                        name: "fk_offline_ops_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_offline_ops_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_channels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    address_ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    address_nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                    address_key_version = table.Column<int>(type: "integer", nullable: false),
                    hint = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    replace_email = table.Column<bool>(type: "boolean", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    code_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    code_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    code_attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_channels", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_channels_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_channels_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_channel_deliveries_group_id",
                table: "channel_deliveries",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_channel_deliveries_next_attempt_at",
                table: "channel_deliveries",
                column: "next_attempt_at",
                filter: "delivered_at IS NULL AND failed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_channel_deliveries_tenant_id_created_at",
                table: "channel_deliveries",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_channel_deliveries_user_id",
                table: "channel_deliveries",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_channel_settings_tenant_id_kind",
                table: "channel_settings",
                columns: new[] { "tenant_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_offline_ops_tenant_id",
                table: "offline_ops",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_offline_ops_user_id_created_at",
                table: "offline_ops",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_user_channels_tenant_id_kind",
                table: "user_channels",
                columns: new[] { "tenant_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "ix_user_channels_user_id_kind",
                table: "user_channels",
                columns: new[] { "user_id", "kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "channel_deliveries");

            migrationBuilder.DropTable(
                name: "channel_settings");

            migrationBuilder.DropTable(
                name: "offline_ops");

            migrationBuilder.DropTable(
                name: "user_channels");
        }
    }
}
