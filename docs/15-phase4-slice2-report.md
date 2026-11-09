# 15 — Phase 4 · Slice 2 Report: Registration, Verification, Password Recovery, Invitation Acceptance (.NET)

Status: **implemented and verified at the scope below. Not production-ready. The UI is still not connected to the API.**

## 1. What was added
| Requirement | Endpoint(s) | Behaviour |
|---|---|---|
| AUTH-002 Registration | `POST /api/auth/register` | **404 unless `registrationMode=open`** (default invitation-only). Name/email validation, password policy, creates a *pending* user with the default `user` role only (cannot grant privileges), emails a verification link. **Always `202` with the same body** — an existing address gets a notice email (with a reset link if the account is active) and no new row |
| AUTH-003 Verification | `POST /api/auth/verify-email`, `POST /api/auth/verify-email/resend` | 256-bit single-use token, **hash only in DB**, 24 h TTL, atomic consumption (exactly one concurrent winner), new link invalidates older ones, expired/used/unknown all return the same `410 expired`. Activates *pending* accounts only (a suspended account stays suspended). Resend is silent for unknown/active/blocked accounts |
| AUTH-004 Forgot password | `POST /api/auth/forgot-password` | identical `202` for known/unknown/blocked; email only to *active* users (pending users get a verification link instead); 30 min token; per-IP and per-account rate limits that apply equally to unknown addresses |
| AUTH-005 Reset password | `POST /api/auth/reset-password` | **policy checked before the token is consumed** (a weak password does not burn the link); atomic single-use; clears lockout; **revokes every session**; audit + security event + "password changed" email; suspended users' links stop working |
| INV-003 Acceptance | `GET /api/invitations/lookup`, `POST /api/invitations/accept` | states `valid/expired/used/revoked/invalid` (email & roles revealed only for valid tokens); acceptance is one transaction: consume invitation → create or activate a *pending* placeholder → apply **exactly the invited roles** → mark verified; never takes over an existing active account (rolled back); policy checked first |
| Password policy | shared | min length from settings (default 12, max 128), common-password list, rejects name/email fragments, low-variety, and **breached passwords** (HIBP range API, k-anonymity, 2 s timeout, **fails open**) |
| Email | `IEmailSender` + queue | console adapter (dev; bodies only with `Email:LogBodies`), **Resend adapter** (bearer key from config, provider error bodies never surfaced), in-process queue + background dispatcher with 3 retries; templates HTML-encode every variable and never contain passwords; links use the configured origin, never the Host header |
| Hardening | startup | Production refuses to start with the console sender, missing Resend key, or `LogBodies=true` |

## 2. Verification evidence
`dotnet test` → **105 passed, 0 failed** (35 new) against real PostgreSQL 16:
- Registration: closed mode 404; pending user, lower-cased email, Argon2id hash, single `user` role; token stored hashed and link built from configured origin; duplicate → identical response + owner notice + no row; policy cases (short, common, personal info, breached, overlong, low-variety); field validation; rate limit 429 + `Retry-After`; pending user cannot log in until verified.
- Verification: single use; 24 h expiry; expired ≡ unknown (identical body); **6 concurrent verifications → exactly 1 success**; resend invalidates older link and is silent for unknown/active/suspended; suspended stays suspended.
- Forgot/reset: identical responses for known/unknown/suspended; emails only for active; token hashed with 30-min TTL; per-account limiter hits unknown addresses equally; reset changes password, kills the live session, clears lock, invalidates old password, sends notice, writes security event, **no password in audit**; weak password doesn't consume token; single use; expiry; **concurrent reset → exactly 1 success**; newer link invalidates older; suspended-after-request stops working; pending users get verification instead.
- Invitations: lookup states and information hiding; accept creates active, verified user with exactly invited roles and records `accepted_user_id`; **concurrent accepts → exactly 1**; expired/revoked/used rejected without creating a user; weak password/name doesn't burn the invite; pending placeholder activated with invited roles; **existing active account is never taken over and the invitation stays pending**; per-IP rate limit.
- Adapters (fake HTTP): templates encode `<script>`/attribute-breaking input; Resend sends bearer JSON and leaks neither key nor provider body; HIBP sends only a 5-char prefix, detects matches, fails open on error/timeout; dispatcher retries then succeeds.
- Meta-test updated: the anonymous surface is **exactly** the 8 expected public endpoints; any other `/api` endpoint must declare authorization.

**Manual end-to-end (real API + PostgreSQL in Docker):** register → verification email (link printed only because Development) → login says `verify_email` → verify 200 → verify again 410 → login ok → forgot → weak reset rejected with field error → reset 200 → old session 401 → old password 401 → new password ok → token reuse 410. Duplicate registration returned the byte-identical 202 body. Audit rows present; **0** audit rows contain either password; DB token columns are 64-char hashes.

## 3. Not done / known gaps
- **UI is not wired** to these endpoints (prototype screens still use the in-memory engine). Note for cutover: email links are `/verify-email?token=…`, `/reset-password?token=…` (UI routes already match) and the invitation link will be `/invite?token=…` (prototype currently uses `/invite/[id]`).
- **Email queue is in-memory**: messages queued but unsent are lost if the process stops. A persistent outbox (table + worker) is the planned fix before production.
- Resend adapter is verified against a fake HTTP handler only — **never run against the real Resend API** (no key available here). Deliverability (SPF/DKIM/DMARC) is not verified.
- HIBP lookup is verified with a fake handler; real network behaviour/latency not measured.
- Invitation **creation/resend/revoke**, email-change confirmation, MFA, and all admin endpoints remain for later slices.
- Rate-limit numbers (register 5/15 min per IP, verify 20, forgot 10, reset 10, invite 30/10) are defaults, unreviewed.
- Registration timing: hashing runs on both branches, but a full timing-side-channel test is not part of the suite.

## 4. Next
Slice 3 MFA (TOTP + recovery codes + login/unlock second step) · 4 Users + invitations management · 5 Roles/permissions/matrix · 6 Sessions list, audit/security, dashboard, profile, settings · 7 UI cutover + Playwright.
