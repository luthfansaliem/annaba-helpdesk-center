# 10 — Security Requirements & Threat Model (Phase 3)

> **Update (ADR-013):** the backend is now **ASP.NET Core (.NET)**, not Next.js/TypeScript. Where this document mentions Next.js route handlers, Server Actions, Drizzle, zod or Vercel for the API, read the equivalent in `13-adr-dotnet-backend.md`. Design intent (layers, schema, contracts, security controls) is unchanged.

Status: **proposed design — nothing here is implemented or tested yet.** No claim of production readiness or compliance is made. Final review gate: Phase 5.

## 1. Assets & trust boundaries
**Assets:** credentials (password hashes, MFA secrets, recovery codes), session tokens, one-time tokens, PII (names, emails, phones), role/permission data, audit/security logs, system secrets (encryption key, email key, DB URL).
**Boundaries:** Browser ↔ App (internet), App ↔ Postgres, App ↔ Email provider, CI/CD ↔ Vercel, Operators ↔ production DB.
**Actors:** anonymous attacker, authenticated low-privilege user, malicious/compromised admin, curious insider with DB read, third-party provider compromise.

## 2. Security design
| Area | Design |
|---|---|
| Password storage | Argon2id (m=19 MiB, t=2, p=1 minimum per OWASP; tuned to ~250 ms), per-hash salt, optional app-wide pepper (`SESSION_PEPPER` is separate from the password pepper — two secrets). Rehash on login when params change |
| Password policy | min length from settings (default 12, max 128), no composition theatre/rotation; optional k-anonymity breached-password check; reject password containing email/name |
| Login | uniform response + timing (verify against dummy hash for unknown users); per-IP and per-account throttling; progressive temporary lockout; no user enumeration on login/register/forgot |
| Session | 256-bit random id; DB stores SHA-256 only; cookie `__Host-sid` (Secure, HttpOnly, SameSite=Lax, Path=/); rotate id on login, MFA completion, privilege/password change; idle (30 min) + absolute (12 h; “remember me” 30 d absolute with idle 7 d) expiry; per-session revoke; server-side lock |
| CSRF | SameSite=Lax + strict `Origin` check + per-session CSRF token on mutations |
| Tokens (verify/reset/invite) | 256-bit CSPRNG, hash-only at rest, single-use via atomic update, short TTL (verify 24 h, reset 30 min, invite configurable 7 d), new token invalidates older ones; reset/verify pages send `Referrer-Policy: no-referrer` and are not cached |
| MFA | TOTP (RFC 6238, SHA-1/6 digits/30 s, ±1 step window) with replay protection; secret AES-256-GCM encrypted (key versioned); 10 recovery codes stored hashed, single-use; enrol/disable/regenerate require reauth; failures rate-limited and counted toward lockout |
| Authorization | per-request permission computation, deny-by-default, escalation guards, last-admin invariants (see RBAC doc) |
| Audit/security events | append-only, same transaction, central redaction (keys matching `pass|token|secret|otp|recovery|apikey` and any value from the secret registry), request id, IP, UA |
| Input handling | zod schemas on every endpoint; parameterised queries only; output encoding by React; avatar re-encoded and magic-byte validated; max body sizes |
| Secrets | env vars via Vercel encrypted store; boot-time validation; never in client bundle (`NEXT_PUBLIC_` forbidden for secrets); settings secrets encrypted in DB and write-only in API |
| Transport/headers | HSTS, nonce CSP, `frame-ancestors 'none'`, nosniff, no-store on authenticated responses, strict cookie scope |
| Email | no passwords or raw session data in email; links contain only a one-time token; SPF/DKIM/DMARC required for the sending domain; template injection-safe (escape all variables) |
| Logging | structured logs with request id; the same redactor as audit; no tokens in URLs → none in logs |
| Dependencies/CI | lockfile, `npm audit` + Dependabot, secret scanning, lint/typecheck/tests as required checks, least-privilege deploy tokens |
| Backup/DR | provider PITR; audit tables included; restore drill before go-live |
| PWA | SW never caches HTML/RSC/API (tested) |

## 3. STRIDE threat model
| # | Threat | Category | Where | Mitigation (design) | Residual / verification |
|---|---|---|---|---|---|
| T1 | Credential stuffing / brute force | S, E | login, MFA, unlock | throttles per IP+account, lockout, breached-password check, MFA, alerts via security events | tune thresholds; load/abuse tests |
| T2 | Account enumeration | I | login, register, forgot, resend | generic responses, uniform timing, same email behaviour | timing test in suite |
| T3 | Session theft (XSS/network) | S, E | cookie | HttpOnly+Secure+`__Host-`, nonce CSP, rotation, short idle, device list + revoke | CSP report-only phase first |
| T4 | Session fixation | S | login | issue new session id at login/MFA | test |
| T5 | CSRF | T | mutations | SameSite, Origin check, CSRF token | test negative cases |
| T6 | Token guessing/replay (reset/verify/invite) | S, E | token endpoints | 256-bit, hashed, single-use, TTL, rate limit | test reuse/expiry/race |
| T7 | Horizontal/vertical privilege escalation (IDOR, role abuse) | E | all endpoints | per-request authz, escalation guards, resource checks inside use-case, no client-trusted ids for authority | authz-coverage test + negative tests per endpoint |
| T8 | Last-admin lockout / admin race | D, E | role & status changes | `FOR UPDATE` + DB backstop trigger | concurrent test |
| T9 | Malicious admin tampering with evidence | R, T | audit | append-only DB grants, in-tx writes, admin cannot access maintenance role; external log drain (P2) | DB-grant test; log shipping future |
| T10 | Secret exposure in logs/audit/errors | I | everywhere | central redactor, error mapper, secret registry, tests that grep logs | CI test |
| T11 | MFA secret/recovery theft via DB read | I | DB | AES-GCM with key outside DB, hashed codes | key rotation runbook |
| T12 | MFA bypass (recovery abuse, replay) | S | MFA | code single-use, TOTP step replay guard, reauth to regenerate, alerts on recovery use | test |
| T13 | Locked-session bypass | E | lock screen | lock enforced in session lookup for every request (page, action, API), not UI | test with direct API call while locked |
| T14 | Open redirect via `returnTo` | S, I | login | same-origin relative paths only (unit-tested in prototype) | keep tests |
| T15 | Clickjacking | T | UI | `frame-ancestors 'none'` | header test |
| T16 | XSS via user-controlled fields (name, department, message, audit meta) | T, E | UI | React escaping, no `dangerouslySetInnerHTML`, CSP, email escaping | lint rule + test with payloads |
| T17 | SQL injection | T | DB | parameterised ORM only; no string-built SQL | lint/review |
| T18 | Avatar upload abuse (polyglot/huge) | T, D | upload | type sniffing, re-encode, 256 KB cap | test |
| T19 | DoS (expensive hashing, list endpoints) | D | login, lists | rate limits before hashing, pagination caps, timeouts | load test |
| T20 | Email abuse (spam via forgot/invite) | D, R | email endpoints | rate limit, invite permission, generic responses | monitor |
| T21 | Supply chain compromise | T | deps/CI | pinned lockfile, audit, least-privilege tokens | process |
| T22 | Stale authorization after role change | E | sessions | permissions never cached in cookie; per-request lookup | test |
| T23 | Offline cache leaks data | I | PWA | SW caches only static assets | SW policy tests (present) |
| T24 | Insecure defaults in prod (console email, demo creds) | I | config | boot refuses `console` email + missing secrets in production; no seeded users | boot test |

## 4. OWASP ASVS mapping (target L2 — verification pending)
| ASVS chapter | Covered by |
|---|---|
| V2 Authentication | password policy, Argon2id, MFA, recovery, reset/verify flows, throttling |
| V3 Session management | session design, rotation, revocation, lock, timeouts |
| V4 Access control | RBAC doc invariants, per-request authz, IDOR checks, CSRF |
| V5 Validation/encoding | zod, React output encoding, upload handling |
| V7 Logging/errors | audit/security events, redaction, error envelope |
| V8 Data protection | encryption of MFA/settings secrets, no secrets in URLs/logs |
| V9 Communications | TLS/HSTS |
| V13 API | schema validation, method restrictions, rate limits |
| V14 Config | headers, secrets handling, dependency hygiene |
Conformance is **not** claimed until Phase 5 verification produces evidence per control.

## 5. Open items requiring PO decision
1. Reauthentication requirement for critical permissions (RBAC §3.8) — adopt? *(recommended: yes)*
2. Should security-event acknowledgement have its own permission (`security.review`)? *(recommended: yes)*
3. Remember-me lifetimes (proposed 30 d absolute / 7 d idle).
4. Breached-password check calls an external API (HIBP range endpoint, k-anonymity) — acceptable from the server? *(recommended: yes, fail-open with log)*
5. External log shipping for tamper evidence (P2).
