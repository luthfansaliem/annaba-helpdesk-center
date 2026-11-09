using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    ts = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_label = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    target_type = table.Column<string>(type: "text", nullable: false),
                    target_id = table.Column<string>(type: "text", nullable: true),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ip = table.Column<string>(type: "text", nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    request_id = table.Column<string>(type: "text", nullable: true),
                    meta_json = table.Column<string>(type: "jsonb", nullable: true),
                    diff_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    invited_by = table.Column<Guid>(type: "uuid", nullable: false),
                    message = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invitations", x => x.id);
                    table.CheckConstraint("ck_invitation_email_lower", "email = lower(email)");
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    module = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    is_critical = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.key);
                    table.CheckConstraint("ck_permission_key", "key ~ '^[a-z_]+\\.[a-z_]+$'");
                });

            migrationBuilder.CreateTable(
                name: "rate_limit_buckets",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false),
                    window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    blocked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rate_limit_buckets", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    is_protected = table.Column<bool>(type: "boolean", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "security_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ts = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    detail = table.Column<string>(type: "text", nullable: false),
                    meta_json = table.Column<string>(type: "jsonb", nullable: true),
                    acknowledged_by = table.Column<Guid>(type: "uuid", nullable: true),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_security_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "system_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    value_json = table.Column<string>(type: "jsonb", nullable: true),
                    is_secret = table.Column<bool>(type: "boolean", nullable: false),
                    value_ciphertext = table.Column<byte[]>(type: "bytea", nullable: true),
                    key_version = table.Column<int>(type: "integer", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    email_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_login_count = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_email_lower", "email = lower(email)");
                    table.CheckConstraint("ck_users_name_len", "length(trim(name)) >= 2");
                });

            migrationBuilder.CreateTable(
                name: "invitation_roles",
                columns: table => new
                {
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invitation_roles", x => new { x.invitation_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_invitation_roles_invitations_invitation_id",
                        column: x => x.invitation_id,
                        principalTable: "invitations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_key = table.Column<string>(type: "text", nullable: false),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => new { x.role_id, x.permission_key });
                    table.ForeignKey(
                        name: "fk_role_permissions_permissions_permission_key",
                        column: x => x.permission_key,
                        principalTable: "permissions",
                        principalColumn: "key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "avatars",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_avatars", x => x.id);
                    table.CheckConstraint("ck_avatar_size", "octet_length(bytes) <= 262144");
                    table.CheckConstraint("ck_avatar_type", "content_type in ('image/png','image/jpeg','image/webp')");
                    table.ForeignKey(
                        name: "fk_avatars_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "email_verification_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    new_email = table.Column<string>(type: "text", nullable: true),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_verification_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_email_verification_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "external_identities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_identities", x => x.id);
                    table.ForeignKey(
                        name: "fk_external_identities_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mfa_factors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    secret_ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    secret_nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                    key_version = table.Column<int>(type: "integer", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_step = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mfa_factors", x => x.id);
                    table.ForeignKey(
                        name: "fk_mfa_factors_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mfa_recovery_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "text", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mfa_recovery_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_mfa_recovery_codes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "password_reset_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    request_ip = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_password_reset_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_password_reset_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_active_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    idle_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    absolute_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_reason = table.Column<string>(type: "text", nullable: true),
                    locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    mfa_required = table.Column<bool>(type: "boolean", nullable: false),
                    mfa_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reauth_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    remember = table.Column<bool>(type: "boolean", nullable: false),
                    ip = table.Column<string>(type: "text", nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    device_label = table.Column<string>(type: "text", nullable: false),
                    browser_label = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_credentials",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    password_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    must_change = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_credentials", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_user_credentials_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_profiles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: true),
                    department = table.Column<string>(type: "text", nullable: true),
                    job_title = table.Column<string>(type: "text", nullable: true),
                    language = table.Column<string>(type: "text", nullable: false),
                    timezone = table.Column<string>(type: "text", nullable: false),
                    notify_email = table.Column<bool>(type: "boolean", nullable: false),
                    notify_security = table.Column<bool>(type: "boolean", nullable: false),
                    notify_product = table.Column<bool>(type: "boolean", nullable: false),
                    auto_lock_minutes = table.Column<int>(type: "integer", nullable: false),
                    avatar_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_profiles", x => x.user_id);
                    table.CheckConstraint("ck_profile_language", "language in ('en','id')");
                    table.ForeignKey(
                        name: "fk_user_profiles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_by = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_action_ts",
                table: "audit_events",
                columns: new[] { "action", "ts" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_actor_id_ts",
                table: "audit_events",
                columns: new[] { "actor_id", "ts" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_target_type_target_id",
                table: "audit_events",
                columns: new[] { "target_type", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_ts",
                table: "audit_events",
                column: "ts",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_avatars_user_id",
                table: "avatars",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_email_verification_tokens_token_hash",
                table: "email_verification_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_email_verification_tokens_user_id_created_at",
                table: "email_verification_tokens",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_external_identities_provider_subject",
                table: "external_identities",
                columns: new[] { "provider", "subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_identities_user_id",
                table: "external_identities",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitations_email",
                table: "invitations",
                column: "email",
                unique: true,
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_invitations_token_hash",
                table: "invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mfa_factors_user_id_type",
                table: "mfa_factors",
                columns: new[] { "user_id", "type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mfa_recovery_codes_user_id",
                table: "mfa_recovery_codes",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_token_hash",
                table: "password_reset_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_user_id",
                table: "password_reset_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_permission_key",
                table: "role_permissions",
                column: "permission_key");

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
                name: "ix_security_events_acknowledged_at",
                table: "security_events",
                column: "acknowledged_at",
                filter: "acknowledged_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_security_events_ts",
                table: "security_events",
                column: "ts",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_security_events_user_id_ts",
                table: "security_events",
                columns: new[] { "user_id", "ts" });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_absolute_expires_at",
                table: "sessions",
                column: "absolute_expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_token_hash",
                table: "sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_user_id",
                table: "sessions",
                column: "user_id",
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_created_at",
                table: "users",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_status",
                table: "users",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "avatars");

            migrationBuilder.DropTable(
                name: "email_verification_tokens");

            migrationBuilder.DropTable(
                name: "external_identities");

            migrationBuilder.DropTable(
                name: "invitation_roles");

            migrationBuilder.DropTable(
                name: "mfa_factors");

            migrationBuilder.DropTable(
                name: "mfa_recovery_codes");

            migrationBuilder.DropTable(
                name: "password_reset_tokens");

            migrationBuilder.DropTable(
                name: "rate_limit_buckets");

            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "security_events");

            migrationBuilder.DropTable(
                name: "sessions");

            migrationBuilder.DropTable(
                name: "system_settings");

            migrationBuilder.DropTable(
                name: "user_credentials");

            migrationBuilder.DropTable(
                name: "user_profiles");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "invitations");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
