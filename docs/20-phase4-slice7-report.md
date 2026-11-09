# Phase 4 — Slice 7: UI cutover to the real API + Playwright suite

## What changed
- The in-memory prototype engine (`src/proto/*`) and its 24 engine tests are **removed**. Every screen now talks to the .NET API through `src/lib/api/client.ts` (same-origin `/api/*`, proxied by `src/middleware.ts`).
- API client: lazy per-session CSRF token (refreshed once on rejection), error envelope → `ApiError`, central routing of 401 / 423 (locked) / `mfa_required` / `mfa_enrollment_required`, and a global **re-authentication dialog**: when the server answers `reauth_required` for a critical action, the user is asked for their password (and TOTP if enrolled) and the original request is replayed exactly once.
- Auth pages (login, MFA, lock screen, register, verify e-mail, forgot/reset password, invitation, account states) use the real endpoints. `(app)` layout guards by session state.
- Admin screens: dashboard (admin or self-service by permissions), users overview / all users (server-side search, filter, sort, paging, bulk) / detail / invitations, roles, permissions, access matrix (`expectedVersion`, conflict → reload), sessions, audit (+ detail), security events (acknowledge), profile (avatar upload, password, e-mail change, MFA enrol with QR + recovery codes shown once), personal settings, system settings (write-only e-mail API key).
- Role picker / role editor / matrix disable what the signed-in user cannot grant (server enforces the same rule).
- Backend: `GET /api/roles` now includes `permissions[]` (used for the grantable check).

## Defects found by the new tests and fixed
| Defect | Effect |
|---|---|
| `Overlay` re-ran its focus effect on every parent render | focus jumped back to the first field while typing in any drawer/dialog (OTP boxes kept only the first digit) |
| Right-hand drawer taller than the viewport | footer buttons (Send invitation / Save role) unreachable |
| Colour contrast (muted search text, delta, pills, danger button) and search-button accessible name | axe serious/critical on every signed-in page |
| Dashboard cards widened by long e-mail addresses | horizontal overflow at 390 / 375 px |
| Footer text "Prototype build" on auth pages | removed |

## Verification
| Check | Result |
|---|---|
| `tsc --noEmit`, `next lint`, `next build` | clean |
| Vitest (`src/**`) | 28 tests: UI components, service worker, **API client** (CSRF attach/refresh, envelope, handlers, reauth replay, raw bodies) |
| Playwright (`npm run test:e2e`, Chromium) | **51 passed** (auth 8, admin 8, rbac 5, mfa 1, navigation 5, quality 24) |
| `dotnet test` | **257 passed** (re-run after adding `RoleListItem.Permissions`) |

## Playwright suite (`e2e/`)
Runs against a real stack: API on :5180 with a throw-away database `annaba_digital_e2e` (recreated on every run), Next production server on :3100, e-mails captured from the console sender log.
- `auth` — returnTo, open-redirect rejection, generic login error, sign-out revokes server session, lock screen (423 until password), registration closed, forgot/reset (single-use link, password policy), proxy ignores forged key.
- `admin` — add user → invitation e-mail → accept → sign in; list search; suspend/activate (sessions revoked); role create + matrix change with re-auth dialog + audit; protected role locked; system settings; all-sessions view; CSRF/Origin rejection.
- `rbac` — plain user, auditor and user-admin visibility and server-side denial.
- `mfa` — enrol with a TOTP computed in the test, recovery codes shown once, second step on next sign-in (wrong code rejected, valid code accepted).
- `quality` — axe (WCAG 2.1 A/AA, serious+critical) on 3 public and 14 signed-in pages; no horizontal overflow at 1440 / 1366 / 1024 / 768 / 390 / 375 px on key pages.
- `navigation` — accordion follows active page, collapse persists, ⌘/Ctrl-K palette, mobile drawer, 404.

## Run it
```bash
npm run db:up
npx playwright install chromium   # once
npm run build && npm run test:e2e
```
Needs Docker (container `annaba-digital-dev-db`) and the .NET SDK.

## Honest gaps
- Axe finds only what automation can: keyboard-only and screen-reader passes, zoom/reflow at 400 %, and RTL are not covered. Only Chromium is tested.
- Responsive checks assert "no page-level horizontal overflow", not visual quality; wide tables scroll inside their container by design.
- E2E does not cover: Resend e-mail adapter, HIBP outage behaviour, recovery-code sign-in via UI, avatar upload, bulk actions, role archive/duplicate, idle/absolute session expiry, multi-tab CSRF refresh, the PWA/offline path against the real API.
- ~~The e-mail queue is still in-memory~~ — now a durable Postgres outbox, see `21-hardening-report.md`.
- The Vercel preview has no backend (`/api` returns 404 without `API_URL`); a UI preview needs a reachable API.
- Pending product items unchanged: D-06 single-tenant, D-08 density reference, UX scorecard (doc 05) unfilled, hosting confirmation.
