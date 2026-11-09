# 14 — Phase 4 · Slice 1 Report: Foundation + Authentication Core (.NET)

Status: **implemented and verified at the scope below. Not production-ready. The UI is NOT yet connected to this API.**

## 1. What exists (verified)
| Area | Delivered |
|---|---|
| Solution | `api/` — Domain / Application / Infrastructure / Api / Tests (net10.0) |
| Database | Full ERD as EF Core migrations (20 tables), seed of 17 permissions, 4 system roles, default settings; **append-only audit log enforced by DB triggers** |
| Security services | Argon2id (m=19 MiB, t=2, p=1, optional pepper, rehash-on-login), 256-bit tokens stored only as SHA-256, per-session CSRF token (HMAC), Postgres atomic fixed-window rate limiter, central redaction |
| Auth endpoints | `POST /api/auth/login` (public), `logout`, `GET /me`, `GET /csrf`, `POST /lock`, `/unlock`, `/reauth`, `GET /health` |
| Session | opaque cookie (`sid`, or `__Host-sid` on https), HttpOnly, SameSite=Lax; idle + absolute expiry; remember-me; rotation at login (fixation defence); revoke on logout; suspended users cut off on the next request |
| Server-side lock (AUTH-006) | `sessions.locked_at`; every endpoint without `AllowLocked` metadata returns **423**; unlock needs the password; 5 failures revoke the session |
| Authorization | deny-by-default `FallbackPolicy`; `RequirePermission(...)` policies; permissions resolved **per request from the DB** (never in the cookie); denials audited with the missing permission; archived roles / non-active users grant nothing |
| Hardening | proxy-key gate (API unreachable except via UI proxy), strict Origin check, security headers, `no-store` on API, error envelope without internals, production startup checks |
| UI bridge | `src/middleware.ts` same-origin proxy (adds proxy key, overwrites any client-supplied one) |

## 2. Verification evidence
**`dotnet test` → 70 passed, 0 failed** against real PostgreSQL 16 (Testcontainers):
- Login: hardened cookie flags; only token **hash** stored; Argon2id format; identical response for unknown email vs wrong password; failed attempts audited **without** the password; lockout after 5 and auto-expiry; per-IP 429 + `Retry-After` (other IPs unaffected); pending/suspended/deactivated give no session and **don't reveal state on wrong password**; session fixation (old token dies); logout revokes server-side; idle/absolute/remember expiry with a controllable clock; suspension cuts the session immediately; rehash on weaker params; validation errors; safe `returnTo`; MFA-enrolled users get an unusable (403 `mfa_required`) session.
- Lock/CSRF: locked session blocked on page-like, mutating and reauth endpoints (423) but `me/csrf` allowed; unlock needs password; 5 failed unlocks revoke; missing/foreign Origin → 403 (even anonymous); missing/forged/other-session CSRF token → 403; no proxy key → 404; security headers; error envelope has no internals.
- Authorization: **every `/api` endpoint declares access explicitly (meta-test)** and the only anonymous one is `login`; 401 vs 403; denied attempts audited; seed roles match the RBAC matrix for 8 role/endpoint pairs; **permission removal applies on the next request with no re-login**; archived role grants nothing; DB rejects unknown permission keys.
- Database: seed idempotent; email lower-case + unique; one pending invitation per email; **UPDATE/DELETE/TRUNCATE on `audit_events` rejected by the database**, purge allowed only in the maintenance context; unique session token hash.
- Unit: Argon2 (salt, pepper, malformed hashes never verify/throw, rehash detection), tokens, redaction, redirect safety.

**Manual end-to-end (real processes):** PostgreSQL in Docker + API (`dotnet run`, Development) + Next.js (`next start`) through the same-origin proxy: wrong password → 401 envelope; login → 200 with HttpOnly cookie; `/me`; logout without CSRF → 403; client-forged proxy key ignored; lock → 423 on reauth; wrong unlock → 401; unlock → 204; audit rows present in the DB; direct API call without proxy key → 404; `UPDATE audit_events` → rejected by trigger.

## 2b. Prototype checks still passing
Frontend: `npm test` (42), `tsc`, `next lint`, `next build` unaffected by this slice.

## 3. Not done / known gaps (do not treat as complete)
- **UI is not wired to the API** — the screens still use the prototype's in-memory engine. Cutover is slice 7.
- **MFA:** schema exists; TOTP enrol/verify/recovery codes and the second step of login/unlock are **not implemented** (users with a confirmed factor are fail-closed: `mfa_required` / unlock refused).
- Not implemented yet: registration, email verification, forgot/reset password, invitations, users/roles/matrix/sessions-list/audit/security/dashboard/profile/settings endpoints, email sending, avatar storage, retention job, last-super-admin DB trigger.
- No Playwright/axe suite; no load testing; no external security review. Rate-limit thresholds are defaults.
- `X-Forwarded-For` is trusted only because the proxy key authenticates the proxy; real deployments must also restrict API network access (Azure ingress rules).
- Argon2 library (Konscious, managed) chosen per ADR-013 proposal — not benchmarked on the target host; parameters need tuning to ~250 ms there.
- Hosting on Azure and .NET 10 LTS are still **proposed**, pending PO confirmation.

## 4. Next slices
2 Registration/verification/forgot-reset/invitation acceptance · 3 MFA · 4 Users+invitations management · 5 Roles/permissions/matrix · 6 Sessions list, audit/security, dashboard, profile, settings · 7 UI cutover + Playwright.
