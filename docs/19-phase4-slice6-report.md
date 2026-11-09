# 19 — Phase 4 · Slice 6 Report: Sessions, Monitoring, Dashboards, Profile & Settings (.NET)

Status: **implemented and verified at the scope below. Not production-ready. The UI is still not connected to the API.**

## 1. What was added
| Area | Endpoints (permission) | Behaviour |
|---|---|---|
| **My sessions** (SES-001) | `GET /api/sessions/me`, `DELETE /api/sessions/me/{id}`, `DELETE /api/sessions/me[?except=current]` (any signed-in user) | device/browser labels, `current` flag, no tokens; revoke one / all others / sign out everywhere (cookie cleared when this session ends); other people's ids answer exactly like unknown ones |
| **All sessions** | `GET /api/sessions` (`sessions.read_all`) | paged (≤100), filter by user, token-free |
| **Audit log** (AUD-001) | `GET /api/audit`, `GET /api/audit/{id}` (`audit.read`) | filter by text/outcome/category/actor/date range, paged, newest first; detail adds IP, user agent, request id, redacted metadata and the before/after diff; **no write endpoints exist** |
| **Security events** (SEC-001) | `GET /api/security-events`, `POST …/{id}/ack` (`security.read`) | filter severity/acknowledged; acknowledgement is single-shot (concurrent reviewers → one success), audited with the reviewer |
| **Retention** (SEC-002) | background worker (daily) + `MonitoringService.PurgeAsync` | deletes audit/security events older than `auditRetentionDays`, expired tokens (>7 d), revoked/expired sessions (>30 d), stale rate-limit buckets; audit deletion happens **only inside the DB maintenance context** (`SET LOCAL annabadigital.allow_audit_purge`) and the purge records itself (`retention.purge`) |
| **Dashboards** (DSH-001…005) | `GET /api/dashboard/admin`, `GET /api/dashboard/me` | every figure is computed from the database; **each widget is present only if the caller holds the permission behind it** (auditor: no invitation data; user admin: no login/security data; plain user: empty) plus `visible[]` and permission-derived `quickActions`; user dashboard returns only the caller's own data incl. profile completion |
| **Profile** (PRF-001/002) | `GET/PATCH /api/me/profile` | validated, audited diff, optimistic version, completion % with what's missing |
| **Avatar** | `PUT/DELETE /api/me/avatar`, `GET /api/avatars/{userId}` | raw-body upload, **bytes inspected** (PNG/JPEG/WebP magic + header-parsed 32–2048 px + ≤256 KB; declared type ignored); served as the exact image type with `nosniff` + `CSP: default-src 'none'; sandbox`; visible to the owner and to holders of `users.read` |
| **Password** | `POST /api/me/password` | **fresh re-authentication (≤5 min, always)**, policy + "must differ", signs out *other* sessions, security event, notice email |
| **Email change** | `POST /api/me/email`, confirm via `POST /api/auth/verify-email` | fresh reauth; confirmation link goes to the **new** address, a notice to the **old**; the address switches only on confirmation (re-checked still free at that moment); generic answer whether or not the address is taken; rate-limited 3/h; a newer request cancels the older link |
| **Preferences** (SET-001) | `GET/PATCH /api/me/preferences` | language (en/id), IANA time zone, notification toggles, auto-lock minutes ≤ the organisation idle limit |
| **System settings** (SET-002) | `GET/PATCH /api/settings` (`settings.manage`), `GET /api/settings/public` (anonymous) | cross-field validation (e.g. absolute lifetime ≥ idle), audited diff, fresh reauth, takes effect immediately (lockout threshold, password length, MFA policy…); the public endpoint exposes only `registrationOpen`, `passwordMinLength`, `mfaPolicy` |
| **Email API key** | `PUT/DELETE /api/settings/email-api-key` | **write-only**: AES-256-GCM encrypted (nonce‖ciphertext), never returned (UI sees `emailApiKeySet` + `source`), never audited; the Resend sender prefers the stored key over configuration |

### Deviations from the prototype / earlier design (please review)
1. **Email provider is not switchable at runtime** (console vs Resend is deployment configuration, shown read-only); only `emailFrom` and the API key are runtime settings.
2. **Avatars are validated, not re-encoded** (no imaging library with a suitable licence was introduced). Mitigations: strict byte inspection, exact content type, `nosniff`, sandbox CSP. A polyglot file with a valid image header is stored but can never be rendered as HTML by the browser.
3. **Password change needs a fresh reauth instead of a `currentPassword` field** (reauth already proves password + second factor).
4. `users.read` holders may view avatars of all users; others only their own.

## 2. Verification evidence
`dotnet test` → **257 passed, 0 failed** (39 new) against PostgreSQL 16:
- **Image inspector (unit):** valid PNG/JPEG/WebP (lossy, lossless, extended) parsed for type + dimensions; rejects HTML, SVG, GIF, EXE-as-PNG, empty, truncated, missing IHDR/SOF, RIFF-but-WAVE, bad VP8 start code, too small/large/zero dimensions; limit enforced to the exact byte.
- **Sessions:** own list only (no other user's, no tokens); revoke one/current (signs out)/others/everywhere; foreign or unknown id → identical 404; CSRF required; admin list gated, paged, token-free.
- **Audit/security:** filters, paging, ordering, wildcard-safe search, bad filter → 422; detail has diff + request id and no secrets; **no write routes**; ack is audited, single-shot, **race → exactly one success**.
- **Dashboard:** every KPI equals a database count; growth series ends at the real total; widget gating verified for Super Admin / Auditor / User Admin / plain user; the user dashboard contains nothing organisation-wide.
- **Retention (isolated DB):** old audit/security rows, expired tokens, old revoked sessions and stale buckets are purged, recent rows kept, purge is self-recorded, honours a changed retention setting, and **ordinary `DELETE` on the audit table is still rejected** afterwards.
- **Profile:** audited diffs, versioning, no-op keeps version, validation, can't touch others; avatar store/replace/remove with safe headers, 6 hostile uploads and an oversized file rejected (nothing stored), visibility rules.
- **Password/email:** reauth gate and expiry; other sessions revoked, old password dead, notices sent, no password in audit; email switches only after confirmation, old address notified, single-use token, taken addresses give the same answer and no mail, **race for the same address → one winner**, newer link cancels older, rate limit.
- **Settings:** only `settings.manage`; 11 invalid/inconsistent inputs rejected with field errors and nothing changed; audited diff; effect proven immediately (lockout threshold lowered → account locks at 4); reauth gate; **API key encrypted at rest, absent from every response, audit and security log**, decrypted only for the sender; invalid keys rejected; all 17 self-service/monitoring endpoints → 401 anonymous, CSRF on writes.

**Manual end-to-end (real API + PostgreSQL, a real 64×64 PNG):** uploaded and fetched byte-identical with `image/png`, `nosniff`, sandbox CSP; HTML disguised as PNG → 422; email change without reauth → 403, with reauth → 202, mails to both addresses, confirm → login with the new address works; dashboards: Auditor sees exactly its 11 widgets, Super Admin sees all KPIs and 6 quick actions, plain user sees `{}`; audit list shows the email-change events; settings change takes effect and is reverted; stored key row = 52 encrypted bytes, `value_json` null, **0 audit rows contain the key**; sign-out-everywhere → `/me` 401.

## 3. Not done / known gaps
- **UI is not wired.** Mapping notes: prototype "Settings" page → `/api/me/preferences`; prototype "System Settings" → `/api/settings`; QR/MFA still slice 3 endpoints.
- **Login-activity chart** uses real login events, but only from the retention window and only UTC days; no per-hour or per-country view, no geo-IP ("Location" column in the prototype's session list is not available — only IP).
- **Idle auto-lock** is stored but **not enforced** by the server (it is a client behaviour); server idle expiry is the system setting.
- **Avatar not re-encoded** and not virus-scanned; no CDN/caching (responses are `no-store`).
- **Retention worker is in-process**: it runs on every instance (idempotent, but wasteful); a single scheduled job (Azure Container Apps job / cron) is preferable in production. Not tested under multiple instances.
- Settings are read from the DB on every request (no cache). Email notifications still use the non-durable in-memory queue.
- Language setting has no effect on server-generated emails (English only).
- No Playwright/axe/load tests; `requireReauthForCritical`, the hierarchy rules and the avatar-visibility rule need PO confirmation.

## 4. Next
**Slice 7 — UI cutover:** replace the prototype's in-memory engine with the real API screen by screen (login/MFA/lock, users, roles/matrix, sessions, audit/security, dashboard, profile/settings), add the Next.js API client + auth guard, and a Playwright suite (journeys, accessibility with axe, responsive widths).
