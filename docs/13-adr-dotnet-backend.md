# 13 — ADR-013: Backend API in .NET (supersedes parts of Phase 3)

Status: **Accepted by Product Owner directive (2026-11-05: "API-nya pakai .NET").** Details marked *(proposed)* still need PO confirmation.

## Context
Phase 3 assumed a TypeScript backend inside Next.js (Route Handlers/Server Actions, Drizzle, Vercel). The PO decided the API must be built with .NET.

## Decision
| Topic | New decision | Replaces |
|---|---|---|
| Runtime | **ASP.NET Core on .NET 10 (LTS)** *(proposed; SDK 10.0.401 is installed locally. .NET 8 LTS support ends Nov 2026, so 10 is the safer baseline)* | Next.js server code |
| API style | ASP.NET Core **Minimal APIs** with endpoint groups; same contract as `08-api-spec.md` (paths, error envelope, status codes unchanged) | Next Route Handlers |
| Data access | **EF Core 10 + Npgsql**, code-first migrations; PostgreSQL stays (D-01) | D-02 Drizzle |
| Authentication | **Custom opaque-session authentication** implemented as an ASP.NET Core `AuthenticationHandler` (not ASP.NET Core Identity, not JWT) — same reasons as D-03: server-side lock state, instant revocation, per-request permissions, MFA/recovery design | D-03 (implementation only) |
| Password hashing | **Argon2id** via `Konscious.Security.Cryptography.Argon2` (managed library, no native dependency) *(proposed)*; parameters per threat model (m=19 MiB, t=2, p=1 minimum) | `@node-rs/argon2` |
| Authorization | Permission **policies** (`RequirePermission("users.suspend")` endpoint metadata) + `FallbackPolicy = RequireAuthenticatedUser` ⇒ **deny by default**; every endpoint must declare `AllowAnonymous` or a permission (enforced by a test) | custom `requirePermission()` |
| Validation | FluentValidation (or DataAnnotations) at the edge; domain invariants in domain/application | zod |
| Email | `IEmailSender` adapter: `Console` (dev), `Resend` via `HttpClient` (prod) | D-04 (same vendor) |
| Rate limiting | Postgres-backed limiter behind `IRateLimiter` (same as D-05); ASP.NET `RateLimiter` middleware only as coarse per-IP backstop | — |
| Tests | **xUnit** + `WebApplicationFactory` + **Testcontainers (PostgreSQL)**; Playwright stays for E2E/a11y/responsive | D-12 Vitest for backend |
| Hosting | UI stays on Vercel; **API on Azure** — *(proposed)* Azure Container Apps (or App Service) + Azure Database for PostgreSQL Flexible Server + Key Vault. Vercel cannot host .NET | D-09 |

## How the browser reaches the API
```
Browser ──(same origin)──▶ Next.js (Vercel)  ── rewrite /api/* ──▶ ASP.NET Core API (Azure)
         cookie __Host-sid                    adds X-Forwarded-For + X-Internal-Proxy-Key
```
- Keeping `/api/*` **same-origin through a Next.js rewrite** means the session cookie stays first-party (`__Host-sid`, SameSite=Lax) and no CORS is needed.
- The API **only accepts requests carrying the shared proxy key** (hardening, constant-time compare) so the public internet cannot bypass the UI origin checks; it also validates `Origin` against `APP_ORIGIN` for mutations and trusts `X-Forwarded-For` only from the configured proxy.
- CSRF: SameSite + `Origin` check + per-session token (HMAC of session id with server secret) — unchanged.

## Consequences for existing documents
- **06 Architecture:** layers are the same but become .NET projects: `AnnabaDigital.Domain`, `AnnabaDigital.Application`, `AnnabaDigital.Infrastructure`, `AnnabaDigital.Api`, `AnnabaDigital.Tests`. The TypeScript layout (`src/server/*`) is **not** built. Next.js becomes UI-only (+ proxy + prototype until cutover).
- **07 Data model:** unchanged logically. Implementation notes: `citext` → `text` + unique index on `lower(email)` with `email = lower(email)` check (avoids extension dependency); `inet` stored as `text`; token/session hashes stored as `bytea`/hex; EF Core migrations replace drizzle-kit.
- **08 API spec:** unchanged contract. `Server Actions` references are dropped — the UI calls the HTTP API.
- **10 Threat model:** controls unchanged; add T25 *API bypass of the UI proxy* (mitigated by proxy key + network restriction) and T26 *spoofed `X-Forwarded-For`* (mitigated by trusted-proxy configuration).
- **11 Test plan:** replace Vitest backend layers with xUnit; keep the catalogue of test IDs.
- **12 Migration/rollback:** `dotnet ef migrations bundle` (or idempotent SQL script) run as a release step; app rollback = previous container revision.
- The TypeScript **prototype engine stays as the executable spec** until the UI is cut over; its tests are being ported to xUnit slice by slice.

## Implementation slices (Phase 4)
1. **Foundation + authentication core** — solution scaffold, full EF schema + migration + seed, security services, login/logout/me/lock/unlock/reauth, deny-by-default authorization, audit/security events, rate limiting, tests. *(in progress)*
2. Registration, email verification, forgot/reset password, invitations acceptance.
3. MFA (TOTP + recovery codes).
4. Users + invitations management endpoints.
5. Roles, permissions, access matrix.
6. Sessions management, audit/security endpoints, dashboard aggregates, profile and settings.
7. UI cutover (replace `ProtoProvider` with real API client slice by slice) + Playwright suite.

## Open items for the PO
1. Confirm **.NET 10 LTS** (vs .NET 8).
2. Confirm **Azure** as API host (Container Apps recommended) — affects cost and the deployment runbook.
3. Confirm Argon2id library choice (Konscious) — alternative: ASP.NET Core Identity's PBKDF2 hasher (weaker, built-in).
