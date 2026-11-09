# 16 — Phase 4 · Slice 3 Report: Multi-Factor Authentication (.NET)

Status: **implemented and verified at the scope below. Not production-ready. The UI is still not connected to the API.**

## 1. What was added (AUTH-007)
| Capability | Endpoint | Behaviour |
|---|---|---|
| Enrol (step 1) | `POST /api/me/mfa/enroll` | needs a **recent re-authentication (≤5 min)**; creates a 160-bit seed, returns the Base32 secret + `otpauth://` URI **once**; stored **AES-256-GCM encrypted** (96-bit random nonce, row id as AAD, versioned key); restarting enrolment replaces the unconfirmed seed; `409` if already enabled |
| Confirm (step 2) | `POST /api/me/mfa/confirm` | correct code activates MFA and returns **10 single-use recovery codes (80 bits each, shown once)**, stored only as HMAC-SHA256 keyed hashes; rate-limited; session marked MFA-verified; security event + email notice |
| Status | `GET /api/me/mfa` | `{enrolled, confirmedAt, recoveryCodesRemaining, required}` — never returns the seed or codes |
| Second step of sign-in | `POST /api/auth/mfa/verify` | a password-only login now yields a **pre-auth session usable for nothing else** (403 `mfa_required` on every endpoint except verify/csrf/logout); TOTP **or** recovery code; success **rotates** to a brand-new session id and kills the pre-auth one |
| Brute-force defence | — | 5 attempts per pre-auth session then the session is revoked; wrong second factors also count toward the **account lockout** |
| Replay protection | — | accepted step must be > `last_used_step`, advanced by one **atomic conditional UPDATE** (two simultaneous uses of one code: exactly one wins) |
| Recovery codes | verify / unlock / reauth | accepted in any case/with spaces or dashes; consumed atomically; each use raises a Medium security event and emails the user; regenerate (`POST /api/me/mfa/recovery-codes`) needs fresh re-auth and **invalidates the old set** |
| Lock screen & re-auth | `/api/auth/unlock`, `/api/auth/reauth` | MFA users must supply the second factor **in addition to the password** (the fail-closed stub from slice 1 is gone) |
| Disable | `POST /api/me/mfa/disable` | password **and** a code required; deletes seed + codes, signs out **all other sessions**, Medium security event, email; **409 when org policy is "required"** |
| Org policy `required` | all endpoints | a user without MFA gets `mfa_enrollment_required` and is confined to enrolment endpoints (me, csrf, reauth, mfa/*, logout) until they enrol; checked from the DB on every request |
| Crypto | — | own RFC 4226/6238 TOTP (HMAC-SHA1, 6 digits, 30 s, ±1 step, constant-time compare), RFC 4648 Base32, HKDF-derived per-purpose HMAC key; Production refuses to start without a real encryption key |

## 2. Verification evidence
`dotnet test` → **154 passed, 0 failed** (49 new) against real PostgreSQL 16:
- **Crypto (30):** Base32 matches the RFC 4648 vectors; TOTP matches **all RFC 6238 Appendix B SHA-1 vectors** (8-digit values; 6-digit is their low digits); ±1 step skew accepted, ±2 rejected; replay and older-step rejection; malformed/non-ASCII-digit codes rejected; AES-GCM round-trip, fresh nonces, **tamper (ciphertext, tag), wrong-row AAD and wrong-key all fail**; **key rotation** (old version still readable, new writes use new version, retired key ⇒ unreadable); recovery-code format/entropy/normalisation; keyed-hash domain separation.
- **Integration (19):** enrolment needs re-auth and stores only ciphertext (seed absent from DB bytes and from status JSON); wrong confirm code doesn't enable; right code → 10 distinct hashed codes + email + security event; restart replaces seed; confirm rate-limited; pre-auth session can do nothing else and verification rotates the session (old id dead); **replay blocked, next step works; concurrent use of one code by two sessions → exactly one success**; skew tolerance; **5 wrong codes revoke the session and lock the account**; malformed codes never cause 5xx; recovery code single-use + case/format tolerant + alerts + wrong-user/guess rejected; regenerate invalidates old set and respects the 5-min re-auth window; **unlock needs password+second factor (password alone → 422)**; disable needs password+code, wipes secrets, signs out other sessions, notifies; **no seed/recovery code/password in audit or security events**; **a ciphertext swapped into another user's row cannot be decrypted** (fails closed, no detail leaked); org-required policy confinement and no opt-out; all endpoints require auth + CSRF.
- Existing meta-tests still enforce that every `/api` endpoint declares access and that the anonymous surface is unchanged.

**Manual end-to-end (real API + PostgreSQL):** enrol without re-auth → 403 `reauth_required`; re-auth → enrol; DB holds 36 ciphertext bytes (20 + 16 tag) + 12-byte nonce, unconfirmed; **a code from an independent Python TOTP implementation was accepted by the .NET confirm** (cross-implementation check); 10 codes → 10 SHA-256-length hashes; new login → `mfa_required`; pre-auth `/me` → 403; wrong code → 401 `invalid_code`; Python-generated code → 200; `/me` → 200; recovery-code login → 200; same code again → 401; audit shows the MFA events and **0 rows contain the secret or a recovery code**; notification emails queued.

## 3. Not done / known gaps
- **UI is not wired** (prototype MFA screens still run on the mock engine). The API returns the Base32 secret and `otpauth://` URI; the **QR image must be rendered client-side** (no server-side QR library added).
- Only **TOTP**; WebAuthn/passkeys and SMS are out of scope (schema allows a future `webauthn` type).
- Secret re-encryption job for key rotation is **not implemented** (rotation works for reads/new writes; old rows keep the old key version until re-written). Key custody (Azure Key Vault) is a deployment task.
- `reauth_required` freshness uses the session's `reauth_at`; it is not yet applied to other critical actions (users.assign_role, permissions.assign, settings.manage) — that arrives with their endpoints (RBAC doc §3.8, pending PO confirmation).
- Email notices are best-effort via the in-memory queue (see slice 2 gap).
- Settings are read from the DB on every request (extra query per request when computing enrollment policy); a short-TTL cache is planned before load testing.
- No Playwright / axe / real-authenticator-app test (the independent Python implementation is a stand-in); not tested with Google/Microsoft Authenticator on a device.

## 4. Next
Slice 4 users + invitations management (admin) · 5 roles/permissions/matrix · 6 sessions list, audit/security, dashboard, profile, settings · 7 UI cutover + Playwright.
