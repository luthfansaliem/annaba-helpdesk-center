# 12 — Migration, Deployment & Rollback Plan (Phase 3)

> **Update (ADR-013):** the backend is now **ASP.NET Core (.NET)**, not Next.js/TypeScript. Where this document mentions Next.js route handlers, Server Actions, Drizzle, zod or Vercel for the API, read the equivalent in `13-adr-dotnet-backend.md`. Design intent (layers, schema, contracts, security controls) is unchanged.

Status: **proposed.** Applies to the first production introduction of the IAM backend (greenfield: there is no legacy user data in this repository).

## 1. Environments
`local` (Docker Postgres) → `preview` (per-PR Vercel deployment + disposable DB branch) → `staging` (prod-like, seeded) → `production`. Secrets differ per environment; no production data in lower environments.

## 2. Migration policy
- Drizzle SQL migrations committed to git, **reviewed as code**, applied by a dedicated release step (`npm run db:migrate`) with a migrator DB role — never by the running app and never at boot.
- **Expand → migrate → contract:** additive changes first (nullable columns, new tables/indexes `CONCURRENTLY`), deploy code that works with both shapes, backfill, then remove old structures in a later release.
- Every migration has: description, risk level, estimated lock time, **rollback strategy** (down script or forward-fix), and was rehearsed on a staging copy.
- Seed migration is idempotent (`ON CONFLICT DO NOTHING`); permission catalog re-synced on deploy (adds only; removals need an explicit migration).

## 3. Initial release sequence
1. Provision Postgres (PITR on), create roles: `app_rw` (CRUD, **no** UPDATE/DELETE on audit/security tables), `migrator`, `maintenance` (retention purge).
2. Set secrets (`DATABASE_URL`, `APP_ORIGIN`, `ENCRYPTION_KEY`, `SESSION_PEPPER`, email provider key); app boot validation must pass.
3. Run migrations + seed on staging → smoke + UAT → record evidence.
4. Take a DB snapshot, run migrations on production.
5. Bootstrap first Super Admin (`bootstrap:admin`, one-time setup link emailed).
6. Deploy app (promote tested build). Verify health endpoint, login, one admin action, audit row written.
7. Enable cron jobs (token/session cleanup, retention).
8. Monitor 24 h: error rate, auth failure rate, 429s, email bounces.

## 4. Rollback
| Situation | Action |
|---|---|
| Bad app deploy, schema compatible | Vercel instant rollback to previous deployment (seconds) |
| Bad migration (additive) | Roll app back; leave additive schema in place; ship forward-fix |
| Bad migration (destructive/failed mid-way) | Stop traffic (maintenance flag), restore from the pre-migration snapshot/PITR to a new instance, repoint `DATABASE_URL`; accept loss of writes since snapshot (low in a new system; communicate) |
| Compromised secret | Rotate: new `ENCRYPTION_KEY` version (re-encrypt MFA/settings secrets lazily), new `SESSION_PEPPER` (forces re-login), revoke all sessions via script, audit review |
| Lockout of all admins | Break-glass script run by an operator with DB access: re-activates a designated Super Admin and writes an audit/security event (documented, access-controlled, tested on staging) |
**Rule:** destructive down-migrations are avoided; rollback = deployment rollback + forward-fix, with snapshots as the safety net.

## 5. Key & secret rotation
`ENCRYPTION_KEY` is versioned (`key_version` columns). Rotation: add new key → new writes use it → background re-encrypt job → retire old key after verification. Rotation procedure rehearsed on staging before go-live.

## 6. Observability & runbook items
Structured logs (redacted), request ids, alerts on: spike in `login.failed`, `account.locked`, 5xx rate, migration failures, email delivery failures, cron failures. On-call runbook: lockout recovery, mass session revoke, secret rotation, restore from backup.

## 7. Go/No-go checklist
Staging UAT signed · security checklist signed · backup restore rehearsed · rollback rehearsed · break-glass tested · secrets set & validated · DNS/email auth (SPF/DKIM/DMARC) verified · PO explicit approval to deploy to production (**not implied by a successful build**).

## 8. Multi-tenant migration path (future, only after a product decision)
Add `orgs` table + `org_id` (backfilled with a single default org) → composite uniques → RLS policies → `Scope` resolution in `RequestContext` → tenant-aware admin UI. Estimated as a separate project; sizing to be done if D-06 is reopened.
