using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditImmutability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Append-only audit log enforced in the database itself (defence in depth, independent of application code).
            // Retention purge must run inside a transaction that sets: SET LOCAL annabadigital.allow_audit_purge = 'on'.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION audit_events_guard() RETURNS trigger AS $$
BEGIN
  IF TG_OP = 'UPDATE' THEN
    RAISE EXCEPTION 'audit_events is append-only';
  ELSIF TG_OP = 'TRUNCATE' THEN
    RAISE EXCEPTION 'audit_events is append-only';
  ELSIF TG_OP = 'DELETE' THEN
    IF coalesce(current_setting('annabadigital.allow_audit_purge', true), '') <> 'on' THEN
      RAISE EXCEPTION 'audit_events is append-only (purge requires the retention context)';
    END IF;
    RETURN OLD;
  END IF;
  RETURN NULL;
END;
$$ LANGUAGE plpgsql;");
            migrationBuilder.Sql("CREATE TRIGGER audit_events_no_update BEFORE UPDATE ON audit_events FOR EACH ROW EXECUTE FUNCTION audit_events_guard();");
            migrationBuilder.Sql("CREATE TRIGGER audit_events_no_delete BEFORE DELETE ON audit_events FOR EACH ROW EXECUTE FUNCTION audit_events_guard();");
            migrationBuilder.Sql("CREATE TRIGGER audit_events_no_truncate BEFORE TRUNCATE ON audit_events FOR EACH STATEMENT EXECUTE FUNCTION audit_events_guard();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_events_no_truncate ON audit_events;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_events_no_delete ON audit_events;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_events_no_update ON audit_events;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit_events_guard();");
        }
    }
}
