# 11 — Test Plan (Phase 3)

> **Update (ADR-013):** the backend is now **ASP.NET Core (.NET)**, not Next.js/TypeScript. Where this document mentions Next.js route handlers, Server Actions, Drizzle, zod or Vercel for the API, read the equivalent in `13-adr-dotnet-backend.md`. Design intent (layers, schema, contracts, security controls) is unchanged.

Status: **proposed.** Evidence for what exists today is in `05-prototype-review.md` (35 prototype tests + 7 service-worker tests = **42 passing**). Everything below the "Today" column is **not yet written** for the real backend.

## 1. Strategy
Pyramid: many fast unit/domain tests → integration tests against a real Postgres (ephemeral: PGlite or container) → a small set of Playwright end-to-end + accessibility + responsive tests. **Targeted runs per slice during implementation; the full regression, security checks and UAT run only at the release gate** (per brief).

| Layer | Tooling | Scope |
|---|---|---|
| Domain/unit | Vitest | policies, state machines, redaction, password policy, token hashing, TOTP, safe redirect |
| Application/integration | Vitest + Postgres | use-cases with real repositories & transactions (ported from `engine.test.ts`) |
| API/contract | Vitest + route handlers | auth, CSRF, error envelope, rate limits, authz per endpoint |
| E2E | Playwright | journeys F1–F12 (see user-flows doc) |
| Accessibility | axe-core in Playwright + manual keyboard/screen-reader checklist | all screens, both widths |
| Responsive/visual | Playwright screenshots at 1440/1366/1024/768/390/375 + overflow assertions | shell, tables, forms |
| Security | negative tests, authz-coverage test, header tests, dependency audit, secret scan, manual pentest checklist (ASVS L2) | release gate |
| Load (smoke) | k6 | login/list endpoints at 10k users |

## 2. Required test catalogue (by requirement)
| ID | Test | Layer | Today |
|---|---|---|---|
| T-AUTH-01 | login success/failure generic message; uniform timing bound | API | prototype logic only |
| T-AUTH-02 | lockout after N failures, auto-unlock after window, admin unlock | integration | prototype |
| T-AUTH-03 | MFA required flow; wrong/replayed TOTP rejected; recovery code single-use | integration | none |
| T-AUTH-04 | registration closed in invitation-only (404); open mode policy; duplicate email response identical | API | prototype |
| T-AUTH-05 | verify token: valid / expired / reused / concurrent double-use | integration | none |
| T-AUTH-06 | forgot: identical response for known/unknown; reset revokes all sessions; token single-use | integration | prototype (partial) |
| T-AUTH-07 | lock screen: locked session rejected on page, action and API; unlock needs password(+MFA); 5 failures revoke | API | none (UI-level only) |
| T-AUTH-08 | account states (pending, suspended, deactivated, locked) block login with right outcome | integration | prototype |
| T-AUTH-09 | `returnTo` open-redirect cases (`//evil`, `/\\evil`, `http://…`, CRLF) | unit | prototype ✔ |
| T-SES-01 | session cookie flags; rotation at login/MFA/password change; idle + absolute expiry; revoke one/others/all | API | none |
| T-SES-02 | no raw tokens in any response/log | API | none |
| T-USR-01 | user CRUD, duplicate (case-insens.), validation, optimistic-lock conflict | integration | prototype (partial) |
| T-USR-02 | suspend revokes sessions; self-suspend blocked; last Super Admin blocked; **concurrent** removal race | integration | prototype (non-concurrent) |
| T-USR-03 | bulk actions: per-item results, partial failure | API | none |
| T-INV-01 | create/resend/revoke/accept; single pending per email; token reuse/expiry; roles applied atomically | integration | prototype (partial) |
| T-ROLE-01 | CRUD, duplicate, archive rules, protected role immutability | integration | prototype |
| T-PERM-01 | matrix save atomic, diff audited, rejects ungranted perms, own `permissions.assign` removal blocked | integration | prototype ✔ |
| T-PERM-02 | **escalation matrix**: for each non-super role, attempt every critical mutation → expect 403 + audit `denied` | API | prototype (subset) |
| T-AUTHZ-COVERAGE | every route/action is registered with a permission or explicitly `public`/`self`; test fails if a new handler lacks a guard | meta | none |
| T-AUTHZ-STALE | permission removed ⇒ next request denied without re-login | API | none |
| T-AUD-01 | each mutation writes audit in same tx (rollback ⇒ no audit, and vice versa for denied attempts) | integration | prototype (partial) |
| T-AUD-02 | redaction: secrets never in audit/logs/errors (property-style test with secret registry) | unit/integration | prototype ✔ (keys only) |
| T-AUD-03 | app DB role cannot UPDATE/DELETE audit rows | DB | none |
| T-SET-01 | settings validation, secrets write-only, cache invalidation | integration | prototype (partial) |
| T-PRF-01 | avatar validation (type, size, polyglot), email change needs reauth + new-address confirmation | API | none |
| T-SEC-HDR | CSP/HSTS/frame/nosniff/no-store headers present | API | none |
| T-CSRF | missing/forged Origin or token ⇒ 403 on every mutation | API | none |
| T-RATE | each scope hits 429 with `Retry-After` | API | none |
| T-SW | service worker never caches HTML/RSC/API | unit | **✔ 7 tests** |
| T-UI-NAV-D | desktop: group accordion, auto-expand active, collapse ↔ rail, persisted | E2E | manual only |
| T-UI-NAV-M | mobile drawer: open, X, backdrop, Esc, select; focus trap/restore; no horizontal scroll | E2E | manual only |
| T-UI-A11Y | axe: no serious/critical violations on every screen; tab order; focus visible; dialogs trap focus | E2E | none |
| T-UI-RESP | overflow assertion at 6 widths on all screens | E2E | manual scan ✔ (14+12 pages) |
| T-UI-STATES | loading/empty/error/denied/expired states per feature | E2E/component | partial |

## 3. Negative / unauthorized test rules
- For every protected endpoint: (a) unauthenticated → 401, (b) authenticated without permission → 403 + `denied` audit, (c) with permission but invalid input → 422, (d) CSRF failure → 403, (e) after permission revoked → 403.
- Fuzz string fields with HTML/SQL/emoji/very long values; assert encoded output and bounded sizes.

## 4. Test data & environments
Deterministic seed fixtures (reuse prototype dataset); injected `Clock`/`Random`; email adapter = in-memory outbox asserted by tests; **no real emails in CI**. CI: lint, typecheck, unit+integration on every PR; Playwright on PR (chromium) and nightly (webkit/firefox); security checks nightly + release gate.

## 5. Release gate checklist
Full regression green · axe clean · responsive matrix green · security checklist (ASVS L2 subset) signed · dependency audit no high/critical · threat-model residuals reviewed · UAT script executed by PO · rollback rehearsed on staging.
