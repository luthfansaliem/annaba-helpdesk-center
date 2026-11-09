# 04 — Decisions & Assumptions

> **Update:** the Product Owner replied "Setuju, lanjut" to the recommended defaults for D-01…D-14, so they are treated as **approved as recommended**. Exception: **D-08 (density reference) is still outstanding** — no reference was supplied, so the prototype uses the existing 13 px token set. Open questions below remain unanswered.

The repository has **no backend**, so each item below introduces new infrastructure. Per the brief, each is recorded as **pending approval** with a recommended default, rationale and alternatives. Nothing here is implemented.

| ID | Decision | Recommended default | Rationale | Alternatives / trade-offs |
|---|---|---|---|---|
| D-01 | Database | **PostgreSQL** (Neon or Vercel Postgres in prod; Docker locally) | Relational integrity, partial/unique indexes, transactions (needed for role matrix, last-admin invariant), JSONB for audit metadata | SQLite/Turso (simpler, weaker concurrency); MySQL |
| D-02 | ORM / migrations | **Drizzle ORM + drizzle-kit migrations** | Thin, SQL-first, typed, works on serverless | Prisma (richer tooling, heavier engine) |
| D-03 | Authentication approach | **In-house session auth** (opaque random session id → SHA-256 stored in DB; `__Host-` HttpOnly cookie) with Argon2id | Brief needs server-side lock state, per-session revocation, instant permission changes, MFA recovery codes, audit of every event — awkward to bolt onto Auth.js/Clerk; no vendor lock-in | Auth.js (DB sessions) + custom MFA; Clerk/WorkOS (fastest, external dependency & cost). Lucia-style patterns are used as reference only |
| D-04 | Email delivery | **Adapter interface**: `console` (dev), **Resend** (prod) | Simple API, good deliverability; swappable | SES, SMTP/Nodemailer, Postmark |
| D-05 | Rate limiting store | **Postgres-backed limiter** behind an interface | No extra infra; adequate for starter kit | Upstash Redis (better at scale/serverless) |
| D-06 | Multi-tenancy | **Single-tenant now**; no tenant column. Extension point: all repositories accept a `Scope` object; docs describe adding `org_id` + row-level security | Brief forbids silent tenant model | Add nullable `org_id` now (cheaper later, more complexity today) |
| D-07 | Public registration default | **Invitation-only** by default; toggle in System Settings | Safer enterprise default | Open registration default |
| D-08 | Density baseline | PO to provide approved reference; implement as tokens at 100 % zoom | No definition in repo (see audit §6) | Treat current 13 px base as the baseline |
| D-09 | Hosting | **Vercel (Node runtime)** for app, managed Postgres | Already used for demo; Argon2 native module needs Node runtime (not Edge) | Self-host container |
| D-10 | MFA scope | TOTP only (+ recovery codes); WebAuthn = P2 | Matches brief | Add passkeys later |
| D-11 | File storage for avatars | Store small processed images (≤256 KB, WebP) in DB `bytea` for starter kit; adapter for S3/Blob | No extra infra | Vercel Blob / S3 from day one |
| D-12 | Test stack | Vitest (unit/integration w/ ephemeral Postgres via PGlite or container) + **Playwright** (e2e, responsive, a11y via axe) | Covers browser/responsive requirements | Cypress |
| D-13 | Role scoping of "Sessions & Devices" | Own sessions for everyone; others' sessions require `sessions.read_all` | Least privilege | — |
| D-14 | Password policy defaults | min 12 chars, breached-password check (k-anonymity HIBP, optional), no forced rotation (NIST 800-63B) | Current guidance | Complexity rules, 90-day rotation (not recommended) |

## Assumptions (to be confirmed)
A1 Single organisation, English UI first (i18n-ready keys). A2 Email is the unique login identifier (case-insensitive). A3 One account = one set of roles (no per-resource scoping). A4 Admins cannot see other users' passwords/MFA secrets/recovery codes under any circumstance. A5 Timezone stored per user; audit timestamps stored UTC. A6 Audit logs are append-only at the application layer (DB-level immutability is a P2 hardening). A7 Dashboard "login activity" derives from `security_events`/`audit_events`, not a separate analytics store. A8 No real emails are sent from CI/tests.

## Open questions for the Product Owner
1. Approve/adjust D-01…D-14 (especially D-03, D-06, D-07, D-08).
2. Provide the **approved density reference** (screenshots or exact tokens).
3. Required retention defaults for audit/security events (proposed 365 / 730 days).
4. Should "Create active user" (no invitation) exist, or invitation-only for all admin-created users?
5. Do we need self-service account deletion/export (GDPR-style)? Not in current scope.
