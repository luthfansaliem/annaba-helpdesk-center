# 05 — Phase 2 Prototype: Review Guide & Evidence

Status: **Implemented, awaiting Product Owner review.** This is a **UX prototype**: every screen is interactive, but all data lives in browser memory (`sessionStorage`). There is **no backend, database, real email, or real cryptography**. Nothing here is production-ready, and no security guarantee is claimed.

## 1. Run it
```bash
npm install && npm run dev      # http://localhost:3000
npm test                        # 35 tests (business rules + components)
```
Sign in with any persona chip on `/login` (password for every account `Passw0rd!demo`, authenticator code `123456`, recovery code `RCVR-0000-0001`). The dark **PROTOTYPE bar** lets you *View as* another persona (Super Admin / User Admin / Auditor / User) and **Reset** the data.

| Persona | What to look for |
|---|---|
| Sarah Wijaya — Super Admin | everything; cannot suspend herself / last Super Admin |
| Daniel Tan — User Admin | can add users but cannot grant Super Admin / Auditor (role options disabled) |
| Maya Putri — Security Auditor | read-only monitoring, no Add User |
| Rizky Pratama — User | personal dashboard, `Access denied` on admin routes |

Force UI states on any list/dashboard page with `?state=loading`, `?state=error` (Retry works) or `?state=empty`.
Prototype tokens: `/verify-email?token=valid|expired|used`, `/reset-password?token=valid|expired`, `/invite/i1` (pending), `/invite/i3` (used), `/invite/i4` (expired), `/invite/i5` (revoked), `/account/{pending|suspended|deactivated|locked}`.

## 2. Requirement coverage (prototype level)
Legend: ✅ behaves as specified in the prototype · 🟡 simulated / partial · ⬜ not in prototype

| Req | State | Notes |
|---|---|---|
| AUTH-001 Login | ✅ | validation, generic error, lockout counter → rate-limit message, remember me, MFA step, safe `returnTo` (unit-tested) |
| AUTH-002 Register | ✅ | closed in invitation-only mode; open mode has strength meter, policy, terms, enumeration-safe response |
| AUTH-003 Verify email | 🟡 | valid / expired / used / resend-with-cooldown states (tokens are fixture values) |
| AUTH-004/005 Forgot / reset | ✅ / 🟡 | generic response, rate limit after 3, policy, sessions revoked + audited; no real token |
| AUTH-006 Lock screen | 🟡 | lock flag held in app state and enforced on every route (direct URL → `/locked`); **real server-side enforcement is Phase 4** |
| AUTH-007 MFA | 🟡 | enroll, challenge, recovery codes, regenerate, disable w/ reauth; QR is a placeholder, codes are fixtures (no TOTP math, nothing encrypted) |
| AUTH-008 Account states | ✅ | pending, suspended, deactivated, locked, session expired, access denied, invitation expired |
| DSH-001..005 Dashboards | ✅ | KPIs/charts/panels derived from the mock data and gated per permission; login-activity series is sample data |
| USR-001..005 Users | ✅ | server-style search/filter/sort/paging, bulk actions, **+ Add User** (header, top-right; not in sidebar), 6-tab detail, status actions with confirmation, last-admin & self-lockout guards |
| INV-001..003 Invitations | ✅ | create, resend (rotates), revoke, state tabs, acceptance page |
| ROLE/PERM-001..003 | ✅ | roles CRUD/duplicate/archive, catalog, interactive matrix (dirty cells, diff confirm, atomic save), escalation guards, protected roles |
| SES-001 Sessions | ✅ | current/others/all, revoke one/others/everywhere; no tokens shown |
| AUD/SEC-001 | ✅ | audit with diff & detail drawer, redaction; security events with acknowledge; **retention purge (SEC-002) ⬜** |
| PRF/SET-001/002 | ✅ / 🟡 | profile, avatar (type/size checks), password & email change with reauth, notification + personal prefs persisted in app state; system settings with validation, diff confirm, write-only secret |
| NAV-D / NAV-M | ✅ | see §3 |
| i18n (language setting) | ⬜ | preference is stored; UI strings are English only |

## 3. Verification evidence (what was actually run)
Environment: production build (`next build && next start`) in the in-app browser; 2026-11-04.

**Automated** — `npm test`: 35 passing
- `engine.test.ts` (24): deny-by-default and *denied attempts are audited*; privilege-escalation (User Admin cannot grant Super Admin, create/invite into higher role, grant permissions it lacks via role edit, matrix save); protected roles; atomic matrix save; last-Super-Admin & self-suspend invariants; suspending revokes sessions; archived role stops granting; duplicate/validation on users; invitation creation; audit redaction; settings audit never records the API key; login outcomes (generic failure, identical result for unknown email vs wrong password, account-state outcomes, rate threshold); session revoke authorization.
- `components.test.tsx` (11): Button/Badge/Tabs/Toast/Field, DataTable (select-all, empty), Pagination, ConfirmDialog, DonutChart, OtpInput (paste/autofill).

**Manual / scripted browser checks (all passed)**
- Route guard: `/users/all` while signed out → `/login?returnTo=…`; after MFA returns to `/users/all`.
- Add User: opens from header button, focus moves into drawer, validation messages, duplicate email (case-insensitive) rejected, success closes drawer and row appears as *Pending*; sidebar contains **no** "Add User".
- Persona switching: Daniel's role picker disables Super Admin/Auditor; Rizky gets *Access denied* with the missing permission named; nav is filtered.
- Access matrix: toggling shows highlighted cell + unsaved banner; confirm dialog lists the diff; save persists; protected row disabled.
- Lock screen: lock → `/locked`; direct navigation while locked stays on `/locked`; wrong password rejected; password + MFA code unlocks.
- System settings: invalid value rejected, open-registration warning shown, API key never appears in the confirm dialog, page, or `sessionStorage`.
- Audit: forced `?state=error` + Retry; detail drawer shows diff; no secret strings in the log.
- Registration: validation, strength meter, duplicate email returns the *same* "check your inbox" message; closed-mode page after Reset.
- Invitations: `/invite/i2` accept → account activated → re-open shows "already used"; `/invite/i4` shows expired.
- MFA (profile): wrong code rejected, enroll shows 10 recovery codes, regenerate/disable require password (+code).
- Navigation — desktop: groups open/close, active group auto-expands, **Collapse menu** toggles 240 px ↔ 68 px icon rail (state persisted). Mobile (390 px): drawer opens by button, closes by **X, backdrop, Esc, and menu selection**, focus enters drawer and returns to the opener, collapse control hidden, no duplicate "Hide Menu".
- Responsive: iframe-based overflow scan of 14 authenticated pages at **1440, 1366, 1024, 768, 390, 375** and 12 auth pages at 375/768 — **no horizontal page overflow** after fixes (wide tables/matrix scroll inside their own container by design).

**Defects found and fixed during this phase**
1. OTP input dropped digits when a full code was pasted/autofilled → fixed + regression tests.
2. Wide line chart rendered oversized axis text → chart width now configurable.
3. Overflow at 375/390 px on System Settings and User detail tabs → grid collapses to one column, tabs scroll.

## 4. Not verified / known gaps (do not treat as done)
- **No visual regression or pixel comparison against an approved reference** (none exists in the repo — see decision D-08). Density is the current 13 px token set.
- No automated browser (Playwright) or axe accessibility run yet; keyboard behaviour was checked manually for overlays, menus, drawer and OTP only. Screen-reader testing not done.
- Touch/real-device testing not done (emulated widths only).
- Security properties (hashing, CSRF, cookies, rate limiting, timing uniformity, encryption) are **described, not implemented**; they are Phase 4 work.
- Real email, TOTP, QR, file storage, audit retention job, pagination over server data: Phase 4.
- Unit tests cover the mock *engine*; the real server code will need its own suite (the engine tests are written to be portable).

## 5. UX scorecard (to be completed by reviewers — no scores are asserted here)
Target: ≥ 9.5/10 per screen. Reviewers score each dimension 1–10 with a note; evidence = the checks above + live walkthrough.

| Screen group | Visual hierarchy | Clarity of states/errors | Responsiveness | Accessibility (keyboard/focus) | Consistency with Azure Executive | Reviewer / date |
|---|---|---|---|---|---|---|
| Auth (login, register, reset, MFA, lock, status) | | | | | | |
| Dashboards | | | | | | |
| Users (overview, list, add, detail) | | | | | | |
| Invitations | | | | | | |
| Roles, Permissions, Access Matrix | | | | | | |
| Sessions | | | | | | |
| Audit & Security events | | | | | | |
| Profile & Settings | | | | | | |
| System Settings | | | | | | |
| Navigation shell (desktop/mobile) | | | | | | |

## 6. Approval gate
Phase 3/4 do not start until the PO records: (a) approval or change requests for this prototype, (b) decisions D-01…D-14 (and the D-08 density reference) in `04-decisions-and-assumptions.md`.
