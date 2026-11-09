# 18 — Phase 4 · Slice 5 Report: Roles, Permission Catalog & Access Matrix (.NET)

Status: **implemented and verified at the scope below. Not production-ready. The UI is still not connected to the API.**

## 1. What was added (ROLE-001, PERM-001…003)
| Area | Endpoints (permission) | Behaviour |
|---|---|---|
| Roles | `GET /api/roles` (`roles.read`) `?includeArchived` | protected first, then system, then A–Z; permission and user counts; `version` |
| Role detail | `GET /api/roles/{id}`, `GET /api/roles/{id}/users` (`roles.read`) | sorted permissions; paged assigned users (page size clamped to 100) |
| Create | `POST /api/roles` (`roles.create`) | custom roles only; stable generated key `custom_…`; case-insensitive unique name (**also enforced by a DB index**); giving it permissions additionally needs `permissions.assign`, a **fresh re-authentication** and *holding each permission yourself* |
| Update | `PATCH /api/roles/{id}` (`roles.update`) | name / description / permissions with optimistic `version`; **protected roles immutable**; system roles cannot be renamed; permission changes need `permissions.assign` + reauth + only permissions you hold; audit diff + `privilege.change` event |
| Duplicate | `POST /api/roles/{id}/duplicate` (`roles.create`) | copies permissions (not users), unique "(copy N)" names; same grant rules |
| Archive | `POST /api/roles/{id}/archive` (`roles.update`) | custom roles only, and only when **no user holds them**; archived roles disappear from lists/matrix, grant nothing, can't be assigned, and free their name |
| Catalog | `GET /api/permissions` (`permissions.read`) | read-only: 17 permissions with module, description, *critical* flag and which roles hold each (permissions are defined in code and seeded, never editable at runtime) |
| Matrix | `GET /api/access-matrix` (`permissions.read`) | roles × permissions, plus `grantable` (exactly what the caller may add) and an opaque **version fingerprint** |
| Matrix save | `PUT /api/access-matrix` (`permissions.assign`) | `{expectedVersion, changes:[{roleId, add[], remove[]}]}` — **all-or-nothing** in one transaction with the roles row-locked; stale version → 409; concurrent saves have exactly one winner; no escalation; protected roles rejected; cannot remove your own `permissions.assign`; audit diff per role + security event |

### Rules added in this slice (please review)
1. **Role hierarchy:** you may only modify (edit, rename, archive, change in the matrix) a role whose *current* permissions you hold — a delegated editor cannot strip or rewrite a more powerful role. Refusals are audited as `denied`.
2. Changing what a role grants requires **`permissions.assign` + recent reauthentication** (same `requireReauthForCritical` setting as slice 4; default ON, pending PO confirmation).
3. **No self-lockout:** a matrix save that would leave the caller without `permissions.assign` is rejected.
4. **Slice-4 gap closed:** suspending or deactivating a pending placeholder now **revokes its pending invitations**, so it cannot activate itself through an old link (audit records `invitationsRevoked`).

## 2. Verification evidence
`dotnet test` → **218 passed, 0 failed** (23 new) against PostgreSQL 16:
- **Reads:** gating; flags/counts/order; archived hidden unless requested; detail + paged users; 404s; catalog shape and *granted-to*; catalog is read-only (POST/PUT/DELETE → 405).
- **Create:** end-to-end — a super admin creates a role, assigns it to a user whose **live session gains exactly those permissions** (and not others); audit diff + security event; validation (name/description/unknown permission); duplicate names case-insensitive; **4 concurrent creates of one name → exactly 1 role**; `roles.create` alone can create empty roles but not grant; with `permissions.assign` you still can't grant what you lack (denied + audited) and need fresh reauth.
- **Update:** permissions change **takes effect on the very next request** both ways; stale `version` → 409; name clash → 409; no-op keeps version; unknown permission → 422; protected role and system-role rename/archive → 409; a delegated editor is **denied** (4 operations, all audited) on a role stronger than themselves, can't grant what they lack, may remove permissions, and needs `permissions.assign` for permission changes but not for renaming.
- **Duplicate/Archive:** copies permissions not users; unique copy names; weak actor denied; archive blocked while users hold the role (message names the count), then succeeds; archived role leaves list and matrix, can't be assigned (422), name reusable.
- **Matrix:** version stable until a change; auditor sees `grantable` = own permissions and can't save; multi-role save applies atomically, returns the new version equal to a fresh GET, bumps role versions; **a save with any invalid change (protected role, unknown role, duplicate role, unknown permission, add+remove same permission) changes nothing**; stale version rejected; **two concurrent saves with the same version → exactly one wins and the other gets 409**; escalation and stripping a stronger role are denied/rejected and audited; needs `permissions.assign` + fresh reauth; no-op → 422; **self-lockout rejected**.
- **Sweeps:** all 10 endpoints → 401 anonymous, 403 for a plain user (each audited), and CSRF required on every mutation; DB-level case-insensitive unique role names (archived rows excluded).
- **Slice-4 gap:** placeholder suspension revokes its invitation (lookup shows `revoked`, accept → 410) and audits it.

**Manual end-to-end (real API + PostgreSQL; new migration applied to the dev DB):** list roles (Super Admin protected, 17 permissions) → create "Support Desk" → User Admin (lacks permissions) refused → assign it to Rizky: his **live session `/users` went 403 → 200** → matrix save (+sessions.read_all, −users.read) → **403 on his next request** → replaying the old matrix version → 409 → editing the protected Super Admin role → 409 → archiving a role in use → 409 with the user count, 204 after reassignment → read-only role refused on matrix GET/PUT; audit shows Success/Failure rows; 4 `privilege.change` security events.

## 3. Not done / known gaps
- **UI is not wired** (prototype roles/matrix screens still use the mock engine). The prototype's **"Permissions" tab inside a role drawer** maps to `PATCH /api/roles/{id}`; the matrix maps to `GET/PUT /api/access-matrix`.
- Role **system roles other than Super Admin can have their permissions changed** (e.g. User Admin) — by design (operators may tune them), but the seed re-sync never resets them; a "restore defaults" action does not exist.
- No endpoint to **change which role is default** for self-registration (always `user`).
- Permission *removal* from a role does not warn about users who would lose all access (UI should show the affected-user count; the API returns counts via `GET /roles`).
- Matrix fingerprint covers permission sets and the role set, not role names/descriptions.
- Same non-durable email queue, no Playwright/axe/load tests as before. `requireReauthForCritical` and the role-hierarchy rule need PO confirmation.

## 4. Next
Slice 6 my-sessions list, audit/security events API, dashboard aggregates, profile & settings · 7 UI cutover + Playwright.
