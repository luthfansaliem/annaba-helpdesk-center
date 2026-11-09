# 08 — API Specification (Phase 3)

> **Update (ADR-013):** the backend is now **ASP.NET Core (.NET)**, not Next.js/TypeScript. Where this document mentions Next.js route handlers, Server Actions, Drizzle, zod or Vercel for the API, read the equivalent in `13-adr-dotnet-backend.md`. Design intent (layers, schema, contracts, security controls) is unchanged.

Status: **proposed — awaiting PO approval.** REST-style JSON over HTTPS. Same-origin only (no public CORS). Most mutations are also exposed as Server Actions that call the **same use-cases**; this document is the contract for both.

## 1. Conventions
- Base path `/api`. Authentication via `__Host-sid` cookie (HttpOnly, Secure, SameSite=Lax, Path=/). No bearer tokens, no tokens in URLs/logs.
- **CSRF:** all non-GET require `Origin` matching `APP_ORIGIN` and header `X-CSRF-Token` (value from `GET /api/auth/csrf`, bound to the session).
- **Idempotency:** `Idempotency-Key` header optional on create endpoints (24 h dedupe) — recommended for invitations/users.
- **Pagination:** `?page=1&pageSize=10` (max 100) → `{ data: [...], page, pageSize, total }`. **Sorting:** `?sort=name&dir=asc` (whitelisted columns).
- **Success:** `200/201` with JSON body. **No content:** `204`.
- **Error envelope** (never contains stack traces, SQL or token values):
```json
{ "error": { "code": "denied|validation|conflict|invariant|rate_limited|not_found|locked|expired|unauthenticated",
              "message": "Human readable", "fields": { "email": "Enter a valid email address." }, "requestId": "…" } }
```
| code | HTTP |
|---|---|
| unauthenticated | 401 |
| denied | 403 |
| locked (session locked) | 423 |
| not_found | 404 |
| validation | 422 |
| conflict | 409 |
| invariant (business rule, e.g. last Super Admin) | 409 |
| expired (token/session) | 410 / 401 |
| rate_limited | 429 + `Retry-After` |
- Every endpoint below lists its **required permission**. `self` = authenticated user acting on own resource (no permission). `public` = unauthenticated. Anything not listed is denied.
- Every mutation writes an audit event (action name in the last column).

## 2. Auth (AUTH-001…008)
| Method & path | Auth | Purpose | Body → Response | Audit / security event |
|---|---|---|---|---|
| POST `/auth/login` | public, rate-limited | email+password | `{email,password,remember,returnTo?}` → `{status:"ok"|"mfa_required"|"verify_email"|"suspended"|"deactivated"|"locked", next?}` (generic `401 invalid_credentials` on any credential failure; uniform timing via dummy hash) | `auth.login.success/failure`; `login.failed`, `account.locked` |
| POST `/auth/mfa/verify` | pre-auth session | TOTP or recovery code | `{code}` or `{recoveryCode}` → `{next}` | `auth.mfa.verify`; failures count toward lockout |
| POST `/auth/logout` | self | end current session | → 204 | `auth.logout` |
| POST `/auth/register` | public, rate-limited; **404 unless `registrationMode=open`** | self-registration | `{name,email,password}` → always `202 {message}` (same for existing email) | `auth.register` |
| POST `/auth/verify-email` | public (token) | consume token, activate | `{token}` → 200 / `410 expired` | `auth.email.verify` |
| POST `/auth/verify-email/resend` | public, rate-limited | new link | `{email}` → always 202 | `auth.email.resend` |
| POST `/auth/forgot-password` | public, rate-limited | request reset | `{email}` → always 202 | `auth.password.forgot` |
| POST `/auth/reset-password` | public (token) | set password | `{token,password}` → 200; revokes all sessions per policy | `auth.password.reset`; `password.reset` |
| POST `/auth/lock` | self | set `sessions.locked_at` | → 204 | `auth.lock` |
| POST `/auth/unlock` | locked session | reauth | `{password,code?}` → 200 (5 failures ⇒ session revoked) | `auth.unlock` |
| POST `/auth/reauth` | self | refresh `reauth_at` for sensitive ops | `{password,code?}` → 200 | `auth.reauth` |
| GET `/auth/csrf` | self | CSRF token | → `{token}` | — |
| GET `/auth/me` | self | current user + `effectivePermissions[]` (UI only) | → user | — |
| POST `/invitations/accept` | public (token) | accept | `{token,name,password}` → 200 | `invitation.accept` |
| GET `/invitations/lookup?token=` | public | validate token state | → `{state:"valid"|"expired"|"used"|"revoked", email?, roles?}` (reveals nothing for unknown tokens) | — |

## 3. Users (USR-001…005) — all require auth + permission
| Method & path | Permission | Notes |
|---|---|---|
| GET `/users` | `users.read` | filters `q,status,role,verified`; sort `name|status|lastLogin|createdAt` |
| GET `/users/stats` | `users.read` | totals, status distribution, growth series |
| POST `/users` | `users.create` **and** `users.assign_role` | `{name,email,phone?,department?,jobTitle?,roleIds[],mode?:"invite"}` — **invitation only** (the second "create active" mode was dropped in ADR slice 4); creates a credential-less pending placeholder + invitation; roles must be *grantable by actor*; needs recent reauth; audit `user.create` |
| GET `/users/{id}` | `users.read` | includes roles, effective permissions |
| PATCH `/users/{id}` | `users.update` | `{name?,phone?,department?,title?}` + `version` for optimistic lock |
| POST `/users/{id}/suspend` · `/deactivate` · `/activate` | `users.suspend` | invariants: not self, not last Super Admin; suspend/deactivate revokes sessions |
| POST `/users/{id}/unlock` | `users.suspend` | clears `locked_until`, resets counter |
| POST `/users/{id}/resend-verification` | `users.update` | rate-limited |
| PUT `/users/{id}/roles` | `users.assign_role` | `{roleIds[]}` atomic replace; escalation + last-admin guards |
| GET `/users/{id}/sessions` | `sessions.read_all` | no tokens |
| DELETE `/users/{id}/sessions` | `sessions.revoke` | revoke all (or `?sessionId=`) |
| GET `/users/{id}/activity` | `audit.read` | audit rows where actor/target is the user |
| POST `/users/bulk` | per-action permission | `{ids[],action:"suspend"|"resend_verification"}`; returns per-item results (partial success allowed, each audited) |

## 4. Invitations (INV-001…003)
| Method & path | Permission |
|---|---|
| GET `/invitations?status=` | `invitations.read` |
| POST `/invitations` | `invitations.manage` (+ roles grantable by actor) |
| POST `/invitations/{id}/resend` | `invitations.manage` (rotates token, extends expiry) |
| POST `/invitations/{id}/revoke` | `invitations.manage` |

## 5. Roles & permissions (ROLE/PERM)
| Method & path | Permission | Notes |
|---|---|---|
| GET `/roles` | `roles.read` | includes permission count + user count |
| GET `/roles/{id}` | `roles.read` | permissions + assigned users (paged) |
| POST `/roles` | `roles.create` (+`permissions.assign` if permissions given) | |
| PATCH `/roles/{id}` | `roles.update` (+`permissions.assign` if permissions change) | protected roles → `409 invariant` |
| POST `/roles/{id}/duplicate` | `roles.create` | |
| POST `/roles/{id}/archive` | `roles.update` | system roles / roles still assigned → `409 invariant` |
| GET `/permissions` | `permissions.read` | catalog (read-only) |
| GET `/access-matrix` | `permissions.read` | roles × permissions + `grantable[]` for actor |
| PUT `/access-matrix` | `permissions.assign` | `{ changes: [{roleId, add[], remove[]}] , expectedVersion }` (`expectedVersion` = opaque fingerprint string from GET) — **all-or-nothing** transaction; rejects adding permissions actor lacks; protected roles; own `permissions.assign` removal |

## 6. Sessions & devices (SES-001)
| Method & path | Auth |
|---|---|
| GET `/sessions/me` | self — current + others, labels only |
| DELETE `/sessions/me/{id}` | self |
| DELETE `/sessions/me?except=current` | self (revoke all others) |
| DELETE `/sessions/me` | self (sign out everywhere) |
| GET `/sessions` | `sessions.read_all` (paged) |

## 7. Monitoring
| Method & path | Permission |
|---|---|
| GET `/audit?q&outcome&category&from&to` | `audit.read` |
| GET `/audit/{id}` | `audit.read` |
| GET `/security-events?severity&ack` | `security.read` |
| POST `/security-events/{id}/ack` | `security.read` *(review is a read-level operational action; may become its own permission)* |
| GET `/dashboard/admin` | returns **only the widgets the caller may see**, each gated by its permission |
| GET `/dashboard/me` | self |

## 8. Profile & settings
| Method & path | Auth | Notes |
|---|---|---|
| GET/PATCH `/me/profile` | self | name, phone, department, title |
| PUT `/me/avatar` (raw bytes) · DELETE `/me/avatar` · GET `/avatars/{userId}` | self (view: owner or `users.read`) | PNG/JPEG/WebP ≤ 256 KB, 32–2048 px; **bytes inspected, not re-encoded** (slice 6 deviation); served with exact type + nosniff + sandbox CSP |
| POST `/me/password` `{newPassword}` | self + **reauth ≤5 min** (replaces a `currentPassword` field) | revokes other sessions |
| POST `/me/email` | self + reauth | sends confirmation to **new** address, notice to old; switch happens on confirmation |
| POST `/me/mfa/enroll` → `{otpauthUri, secret}` · POST `/me/mfa/confirm` `{code}` → `{recoveryCodes[10]}` (shown once) | self + reauth | |
| POST `/me/mfa/recovery-codes` | self + reauth | regenerate |
| POST `/me/mfa/disable` `{password, code\|recoveryCode}` | self (password + second factor in body) | 409 `invariant` when MFA policy is required |
| GET/PATCH `/me/preferences` | self | language, timezone, notifications, autoLock (≤ system idle) |
| GET `/settings` | `settings.manage` | secrets returned as `{set:true}` |
| PATCH `/settings` | `settings.manage` | validated; diff audited; fresh reauth. `PUT/DELETE /settings/email-api-key` write-only secret; `GET /settings/public` anonymous (registrationOpen, passwordMinLength, mfaPolicy). Email *provider* is deployment config (read-only) |

## 9. Rate limits (defaults; configurable in code)
| Scope | Key | Limit |
|---|---|---|
| login | IP | 20 / 5 min |
| login | account (email hash) | 5 failures → lock `lockoutMinutes` |
| mfa verify | session | 5 failures → revoke pre-auth session |
| forgot / resend / register | IP + email hash | 3 / 15 min each |
| unlock | session | 5 failures → revoke |
| admin bulk | user | 10 / min |

## 10. Security headers (all responses)
`Content-Security-Policy` (nonce-based, no inline scripts except nonce, `frame-ancestors 'none'`), `Strict-Transport-Security` (2 y, includeSubDomains), `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy` (deny camera/mic/geolocation), `Cache-Control: no-store` on authenticated pages and every `/api` response.

## 11. Versioning & compatibility
Unversioned internal API (same-origin, deployed with the UI). If a public API is introduced later it goes under `/api/v1` with token auth as a separate design.
