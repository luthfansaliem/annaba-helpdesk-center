# 06 — Architecture (Phase 3)

> **Update (ADR-013):** the backend is now **ASP.NET Core (.NET)**, not Next.js/TypeScript. Where this document mentions Next.js route handlers, Server Actions, Drizzle, zod or Vercel for the API, read the equivalent in `13-adr-dotnet-backend.md`. Design intent (layers, schema, contracts, security controls) is unchanged.

Status: **proposed — awaiting PO approval.** Based on approved defaults D-01…D-14 (single-tenant, Postgres, Drizzle, in-house session auth). Nothing in this document is implemented yet.

## 1. Principles
1. **Server is the authority.** Every mutation and every read of protected data passes a server-side authorization check; UI gating is cosmetic.
2. **Deny by default.** A route/action without an explicit `requirePermission()` fails the test suite (see test plan T-AUTHZ-COVERAGE).
3. **Thin framework, fat core.** Business rules live in framework-free modules that can be unit-tested without Next.js (the prototype `engine.ts` is the executable spec).
4. **Adapters at the edges.** Email, rate limiter, storage, clock, random and crypto sit behind interfaces so tests are deterministic and vendors are swappable.
5. **Append-only evidence.** Audit/security events are written in the *same transaction* as the change they describe.

## 2. Layers and dependency rule
```
presentation  (Next.js App Router: pages, components, Server Actions, Route Handlers)
      │  calls
application   (use-cases: CreateUser, SuspendUser, SaveAccessMatrix … — orchestration, transactions, authz, audit)
      │  uses
domain        (pure rules: Permission, Role, Policy, PasswordPolicy, state machines, invariants — no I/O)
      ▲  implemented by
infrastructure (Drizzle repositories, argon2, TOTP, email, rate limiter, clock, crypto/KMS, storage)
```
Rule: dependencies point **inward**. `domain` imports nothing from the others; `application` depends on `domain` and *interfaces*; `infrastructure` implements those interfaces; `presentation` only calls `application`.

## 3. Proposed repository layout
```
src/
  app/                       # presentation: routes (existing prototype pages are re-pointed at real use-cases)
    (auth)/  (app)/  api/
  server/
    domain/                  # users, roles, permissions, sessions, invitations, policies, errors
    application/             # use-cases + ports (interfaces)
      auth/ users/ roles/ invitations/ sessions/ audit/ settings/ dashboard/
    infrastructure/
      db/                    # drizzle schema, migrations, repositories, unit-of-work
      security/              # argon2id, token generation/hash, aes-gcm, totp, csrf, redact
      email/                 # console, resend adapters + templates
      ratelimit/             # postgres limiter
      storage/               # avatar store (db bytea adapter)
    http/                    # request context, cookie helpers, error mapper, requirePermission()
  components/ …              # unchanged design system
  proto/                     # kept as executable spec / storybook-like demo until removed
```
`proto/engine.ts` rules are ported 1:1 into `server/domain` + `server/application`; its tests are ported to run against real repositories (same assertions).

## 4. Request lifecycle (protected endpoint)
1. **Edge/middleware**: only cheap checks (cookie present, security headers, redirect unauthenticated page loads). *No authorization decisions are made here.*
2. **Handler** builds a `RequestContext`: reads `__Host-sid` cookie → hashes → loads session row (+user, roles) in one query → rejects if revoked/expired/idle-expired/**locked**/user not `active`.
3. **CSRF**: mutating methods require same-origin `Origin` + per-session CSRF token header (double-submit bound to session).
4. **Authorization**: `requirePermission(ctx, "users.suspend")` computes effective permissions **per request** from DB (no permission claims cached in the cookie/JWT), so role changes apply on the next request.
5. **Use-case** runs inside a transaction (`withTx`) and writes audit + security events before commit.
6. **Response**: errors mapped to stable codes (see API spec); no internal details leaked.
7. **Sliding expiry**: `last_active_at` updated at most once per minute to limit write load.

## 5. Cross-cutting modules
| Concern | Design |
|---|---|
| Authorization | `Policy` objects in domain (`canAssignRole(actor, role)`, `canEditRole`, `assertNotLastSuperAdmin`) used by use-cases; ports `requirePermission` for coarse gate |
| Audit | `AuditPort.record({actor, action, target, outcome, meta, diff})` — redaction function applied centrally; denied attempts recorded too |
| Security events | `SecurityPort.emit(type, severity, userId, detail)`; also drives lockout and dashboard widgets |
| Rate limiting | key = `(scope, ip)` and `(scope, accountId/emailHash)`; scopes: login, mfa, forgot, resend, register, unlock; limiter interface `consume(key, limit, window)` |
| Settings | typed `SystemSettings` loaded through a short TTL cache (≤30 s) with explicit invalidation on update; secrets stored encrypted, never returned by API |
| Config/secrets | env validated by zod at boot (`DATABASE_URL`, `APP_ORIGIN`, `ENCRYPTION_KEY` (32B, base64), `SESSION_PEPPER`, email keys). App refuses to start if missing in production |
| Time/randomness | `Clock`, `Random` ports (tests inject fakes) |
| Errors | `DomainError(code, httpStatus)`; codes: `denied`, `validation`, `conflict`, `invariant`, `rate_limited`, `not_found`, `locked`, `expired` (mirrors prototype `Result.code`) |

## 6. Data access
- Drizzle ORM + SQL migrations (`drizzle-kit generate`, reviewed SQL committed). Repositories return domain types, never ORM rows.
- **Unit of work**: `withTx(fn)` passes a transaction-bound repository set; use-cases never touch `db` directly.
- Matrix save, role assignment, invitation acceptance, user creation = single transactions; invariants re-checked **inside** the transaction with `SELECT … FOR UPDATE` on the affected super-admin rows to avoid races (two admins removing each other).

## 7. Frontend integration
- Pages become Server Components that call application queries (authorization enforced there) and hydrate client components with the result; mutations use Server Actions returning the same `Result` shape the prototype already uses, so existing components keep working.
- `PermissionGate`/`Require` become convenience wrappers over a server-provided `effectivePermissions` array (UI only).
- Prototype `ProtoProvider`, persona bar and mock data are removed behind a build flag at cutover.

## 8. Extension points
- **Multi-tenancy (not implemented — D-06):** repositories accept a `Scope` argument (currently a constant). Adding tenancy later = add `org_id` to tenant-owned tables, composite uniqueness (`org_id, email`), Postgres RLS policies keyed on `current_setting('app.org_id')`, and `Scope` resolution in `RequestContext`.
- **External identities:** `external_identities` table + `IdentityProvider` port for OIDC/SAML.
- **WebAuthn:** `mfa_factors.type` already allows `webauthn`.

## 9. Deployment topology
Vercel (Node runtime — required for native Argon2) + managed Postgres (pooled connection string for serverless) + email provider. Migrations run as a **separate release step**, never at app boot. Cron (Vercel Cron) for retention purge and expired-token cleanup. See `12-migration-and-rollback.md`.

## 10. PWA constraint (implemented)
The service worker caches only public hashed assets and a static offline page; it must never cache HTML, RSC or API responses (enforced by tests in `src/__tests__/sw.test.ts`). Any future offline feature requires a separate security review. **R3-11:** reviewed in ADR-GA-07 (doc 66). Field mode keeps a person's own open work orders and queued changes in IndexedDB (never in the service worker), expires them after 72 hours, wipes them on sign-out, and replays changes with idempotency keys. The rule above is unchanged.
