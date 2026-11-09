# 17 — Phase 4 · Slice 4 Report: User & Invitation Management (.NET)

Status: **implemented and verified at the scope below. Not production-ready. The UI is still not connected to the API.**

## 1. What was added (USR-001…005, INV-001/002)
| Area | Endpoints (permission) | Behaviour |
|---|---|---|
| List | `GET /api/users` (`users.read`) | server-side search (name/email/department, case-insensitive, **wildcards and SQL text treated literally**), filters `status` (incl. derived *locked*), `role`, `verified`, sorting (whitelisted), pagination (page size clamped to 100); never returns credentials |
| Detail / stats | `GET /api/users/{id}`, `/stats` (`users.read`) | roles, effective permissions (status-aware), live-session count, optimistic-lock `version`; totals, distribution, 12-month growth, recent registrations — all computed from the database |
| Create | `POST /api/users` (`users.create` **and** `users.assign_role`) | creates a **credential-less pending placeholder** with the intended roles + a hashed, expiring invitation email; **administrators never set or see passwords**; duplicate/conflict detection incl. concurrent creates; recent re-authentication required (critical) |
| Update | `PATCH /api/users/{id}` (`users.update`) | audited before/after diff, optimistic concurrency (stale `version` → 409) |
| Status | `POST /api/users/{id}/suspend｜deactivate｜activate` (`users.suspend`) | revokes all sessions immediately on suspend/deactivate; cannot activate a *pending* user; **cannot suspend yourself**; **last active Super Admin is protected** |
| Unlock / resend | `POST …/unlock`, `…/resend-verification` | resend rotates the invitation (old link dies) or sends a verification link to self-registered users |
| Roles | `PUT /api/users/{id}/roles` (`users.assign_role`) | atomic replace; **cannot grant a role containing permissions you don't hold**; ≥1 role; cannot remove your own Super Admin role; effective immediately for the target's live session; audit diff + `privilege.change` security event; recent re-authentication required |
| Sessions / activity | `GET/DELETE …/sessions` (`sessions.read_all` / `sessions.revoke`), `GET …/activity` (`audit.read`) | no tokens ever exposed; revoke one or all |
| Bulk | `POST /api/users/bulk` | `suspend` / `resend_verification`, per-item results (partial success), per-action permission, ≤100 ids |
| Invitations | `GET/POST /api/invitations`, `POST …/{id}/resend｜revoke` (`invitations.read/manage`) | derived status (pending/accepted/expired/revoked), configurable lifetime, anti-escalation on invited roles, resend rotates token and revives revoked ones, revoke/accept races have exactly one winner |

### New rules introduced in this slice (please review)
1. **Hierarchy rule:** you cannot act on a user whose effective permissions exceed yours (a *User Admin* cannot suspend, edit, unlock, re-role or sign out a *Super Admin*). Every refused attempt is audited as `denied`.
2. **Re-authentication for critical actions** (`users.assign_role` operations: create user, change roles) via system setting `requireReauthForCritical` (**default ON**). This implements the recommendation in RBAC §3.8, which the PO has **not yet confirmed**; it can be switched off in settings.
3. **"Create user" = invitation.** The prototype's second mode ("create active user") is **not implemented**: an active account without a user-chosen password cannot exist safely. `mode` accepts only `invite`.
4. **Last-Super-Admin invariant** is enforced three ways: application check under `SELECT … FOR UPDATE` row locks, plus a **deferred constraint trigger in PostgreSQL** (new migration `LastSuperAdminGuard`) as backstop.
5. Placeholders without credentials can **never** be activated by an email-verification link or reset flow (slice-2 hardening); only accepting the invitation activates them.

## 2. Verification evidence
`dotnet test` → **195 passed, 0 failed** (41 new) against PostgreSQL 16 (shared DB + **isolated fresh databases** for last-admin tests):
- **Reads:** permission gating; paging/sorting/filters; page-size clamp; unknown filter → 422; wildcard/SQL-text search literal; role and verification filters; detail shape (no credential fields); stats equal DB counts.
- **Create:** placeholder has no credential; invitation hashed (SHA-256 hex), 7-day (setting-driven) expiry, email link built from configured origin; **end-to-end accept → active user with exactly the intended roles who can log in**; reauth required / window expires / setting off; both permissions required; **escalation to Super Admin/Auditor denied, audited, nothing created, mixed lists rejected whole**; duplicate (case-insensitive), pending-invitation conflict, validation fields, unknown role; **4 concurrent creates of one email → exactly 1 user and 1 invitation**.
- **Update:** diff audited (parsed from jsonb), version bumps, stale edit → 409, no-op keeps version, validation, 404.
- **Hierarchy:** 7 different operations by a User Admin against a Super Admin all `403 denied` + audited, target unchanged; a Super Admin can manage a User Admin.
- **Status:** suspend kills live sessions at once, blocks login, audits reason + diff + security event; activate/deactivate transitions; pending can't be force-activated; self-suspend 409 (+ audit failure).
- **Roles:** reauth gate; atomic replace; **target's existing session gains/loses access on its very next request**; audit + `privilege.change`; ≥1 role; cannot promote self; cannot assign above own privileges (no partial change).
- **Last Super Admin (isolated DBs):** only/last admin can't be suspended/deactivated/demoted by an all-permission operator (409 + audit); a second admin lifts the block; **two operators racing to remove the last two admins in 6 rounds → exactly one wins each time, never zero**; **raw SQL** to suspend/unrole/delete the last admin is rejected by the **database trigger**, while a same-transaction swap is allowed (deferred check).
- **Sessions/activity/bulk:** no tokens in responses; revoke one/all; auditor can read but not revoke; activity needs `audit.read`; bulk partial results (outranking target, self, unknown id) and per-action permission; size/action validation.
- **Invitations:** derived expiry & filters; hashed token + configured lifetime; escalation denied; conflicts; delegated inviter limited to ≤ own privileges; expired pending replaced by new invite; resend rotates (old token gone), extends expiry, revives revoked, refuses when another pending exists or sender lacks privileges; revoke only pending and kills the link; **revoke-vs-accept race has one consistent winner (3 rounds)**.
- **Authz sweep:** all 19 endpoints return 401 anonymously and 403 (audited) for a plain user; CSRF required on every mutation; raw tokens never in audit.

**Manual end-to-end (real API + PostgreSQL, new migration applied to the dev DB):** list as User Admin; create without reauth → 403; after reauth → 201 *pending*; invite as Super Admin / Auditor role → 403; User Admin suspending the Super Admin → 403; Super Admin suspending self → 409; person accepts the emailed link → active with role `user`; suspended user's login → `suspended`; bulk shows per-item errors; audit trail contains Success/Denied/Failure rows; direct SQL suspension of the only Super Admin → `at least one active super_admin must remain`; 0 audit rows contain the invitation token.

## 3. Not done / known gaps
- **UI is not wired** (prototype users screens still use the mock engine). Gaps vs the prototype UI: no "create active user" mode (by design, see rule 3); status filter `locked` is derived from `locked_until`.
- Roles/permissions management endpoints (create/edit/archive roles, matrix) are **slice 5**; until then role *assignment* works but roles themselves are only the seeded ones (+ DB-inserted test roles).
- The invitation stays valid if an admin later **suspends/deactivates the placeholder** (the person could still accept and become active). A hardening decision is needed: revoke pending invitations when their placeholder user is suspended/deactivated.
- A revoked invitation leaves its pending placeholder user in the list (admins can deactivate it).
- Bulk actions are sequential (≤100) and each item is audited separately; no async job.
- Search uses `LOWER(...) LIKE`-style containment (fine for thousands of users; a trigram index is the planned optimisation for ≥100k).
- Email notifications still use the non-durable in-memory queue (slice 2 gap). No Playwright/axe/load tests.
- `requireReauthForCritical` default needs PO confirmation; the hierarchy rule is new policy not in the original brief (it prevents privilege abuse) and also needs confirmation.

## 4. Next
Slice 5 roles/permissions/access matrix (+ reauth for `permissions.assign`) · 6 my-sessions list, audit/security events API, dashboard aggregates, profile and settings · 7 UI cutover + Playwright.
