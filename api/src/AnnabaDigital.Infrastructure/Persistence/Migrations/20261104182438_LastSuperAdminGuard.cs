using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LastSuperAdminGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backstop for the "never zero active Super Admins" invariant (the application also enforces it under row locks).
            // Deferred, so a transaction may swap admins as long as the final state still has one.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION enforce_super_admin_exists() RETURNS trigger AS $$
DECLARE sa uuid; remaining int;
BEGIN
  SELECT id INTO sa FROM roles WHERE key = 'super_admin';
  IF sa IS NULL THEN RETURN NULL; END IF;
  IF TG_TABLE_NAME = 'user_roles' THEN
    IF OLD.role_id <> sa THEN RETURN NULL; END IF;
  ELSIF TG_TABLE_NAME = 'users' THEN
    IF OLD.status <> 'Active' THEN RETURN NULL; END IF;
    IF TG_OP = 'UPDATE' AND NEW.status = 'Active' THEN RETURN NULL; END IF;
    -- only a Super Admin being deactivated/removed can violate the invariant
    IF NOT EXISTS (SELECT 1 FROM user_roles WHERE user_id = OLD.id AND role_id = sa) THEN RETURN NULL; END IF;
  END IF;
  SELECT count(*) INTO remaining FROM user_roles ur JOIN users u ON u.id = ur.user_id WHERE ur.role_id = sa AND u.status = 'Active';
  IF remaining = 0 THEN
    RAISE EXCEPTION 'at least one active super_admin must remain';
  END IF;
  RETURN NULL;
END;
$$ LANGUAGE plpgsql;");
            migrationBuilder.Sql("CREATE CONSTRAINT TRIGGER super_admin_guard_roles AFTER DELETE OR UPDATE ON user_roles DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_super_admin_exists();");
            migrationBuilder.Sql("CREATE CONSTRAINT TRIGGER super_admin_guard_users AFTER DELETE OR UPDATE OF status ON users DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_super_admin_exists();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS super_admin_guard_users ON users;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS super_admin_guard_roles ON user_roles;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS enforce_super_admin_exists();");
        }
    }
}
