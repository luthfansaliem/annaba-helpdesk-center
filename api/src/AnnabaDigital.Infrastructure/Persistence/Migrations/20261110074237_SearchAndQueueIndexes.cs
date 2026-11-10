using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SearchAndQueueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ticket search (number, subject, description) matches the middle of words, so a plain index cannot serve it. Trigram indexes on the lower-cased text can,
            // and the queries are written as LIKE on lower(column) to match them. Needs the pg_trgm extension (part of PostgreSQL's standard contrib set).
            // "Any status" views and deep pages of the queue: newest first without sorting the whole table. (Written as SQL because EF would fold it into the partial index above.)
            migrationBuilder.Sql("CREATE INDEX ix_tickets_tenant_id_updated_at ON tickets (tenant_id, updated_at DESC, id DESC);");
            // Deadline view of the queue ("soonest deadline first", often with an SLA state) and a person's notification list, newest first.
            migrationBuilder.Sql("CREATE INDEX ix_tickets_open_due ON tickets (tenant_id, (resolution_due_at IS NULL), resolution_due_at, id DESC) WHERE status NOT IN ('Closed', 'Cancelled', 'Rejected');");
            migrationBuilder.Sql("CREATE INDEX ix_notifications_user_newest ON notifications (tenant_id, user_id, created_at DESC, id DESC);");
            // The reports look at "the last N days": created, resolved and breached tickets of a period are read through these instead of scanning every ticket.
            migrationBuilder.Sql("CREATE INDEX ix_tickets_created ON tickets (tenant_id, created_at);");
            migrationBuilder.Sql("CREATE INDEX ix_tickets_resolved ON tickets (tenant_id, resolved_at) WHERE resolved_at IS NOT NULL;");
            migrationBuilder.Sql("CREATE INDEX ix_tickets_breached ON tickets (tenant_id, sla_breached_at DESC) WHERE sla_breached_at IS NOT NULL;");
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
            migrationBuilder.Sql("CREATE INDEX ix_tickets_number_trgm ON tickets USING gin (lower(number) gin_trgm_ops);");
            migrationBuilder.Sql("CREATE INDEX ix_tickets_subject_trgm ON tickets USING gin (lower(subject) gin_trgm_ops);");
            migrationBuilder.Sql("CREATE INDEX ix_tickets_description_trgm ON tickets USING gin (lower(description) gin_trgm_ops) WHERE description IS NOT NULL;");
            migrationBuilder.CreateIndex(
                name: "ix_tickets_tenant_id_open_updated_at",
                table: "tickets",
                columns: new[] { "tenant_id", "updated_at", "id" },
                descending: new[] { false, true, true },
                filter: "status NOT IN ('Closed', 'Cancelled', 'Rejected')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_tickets_breached;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_tickets_resolved;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_tickets_created;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_notifications_user_newest;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_tickets_open_due;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_tickets_tenant_id_updated_at;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_tickets_description_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_tickets_subject_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_tickets_number_trgm;");
            migrationBuilder.DropIndex(
                name: "ix_tickets_tenant_id_open_updated_at",
                table: "tickets");
        }
    }
}
