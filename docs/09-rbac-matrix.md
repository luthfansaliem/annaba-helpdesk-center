# 09 — RBAC Permission Matrix (Phase 3)

Status: **proposed — awaiting PO approval.** Mirrors the prototype's seed (`src/proto/data.ts`). ● = granted, — = not granted. `super_admin` is protected: its permission set cannot be changed.

## 1. Seed roles × permissions
| Module | Permission | Critical | Super Admin | User Admin | Security Auditor | User | HR Manager *(custom example)* |
|---|---|:-:|:-:|:-:|:-:|:-:|:-:|
| Users | `users.read` | | ● | ● | ● | — | ● |
| Users | `users.create` | | ● | ● | — | — | — |
| Users | `users.update` | | ● | ● | — | — | — |
| Users | `users.suspend` | | ● | ● | — | — | — |
| Users | `users.assign_role` | ✔ | ● | ● | — | — | — |
| Invitations | `invitations.read` | | ● | ● | — | — | ● |
| Invitations | `invitations.manage` | | ● | ● | — | — | ● |
| Roles | `roles.read` | | ● | ● | ● | — | — |
| Roles | `roles.create` | | ● | — | — | — | — |
| Roles | `roles.update` | | ● | — | — | — | — |
| Permissions | `permissions.read` | | ● | — | ● | — | — |
| Permissions | `permissions.assign` | ✔ | ● | — | — | — | — |
| Sessions | `sessions.read_all` | | ● | ● | ● | — | — |
| Sessions | `sessions.revoke` | | ● | ● | — | — | — |
| Monitoring | `audit.read` | | ● | — | ● | — | — |
| Monitoring | `security.read` | | ● | — | ● | — | — |
| Administration | `settings.manage` | ✔ | ● | — | — | — | — |

Self-service (no permission, always limited to own data): profile, avatar, password/email change, MFA, own sessions, own activity, preferences, lock/unlock.

## 2. Route → permission (UI nav & API)
| Screen | Permission to view | Extra for actions |
|---|---|---|
| Dashboard (admin widgets) | any of `users.read`, `audit.read`, `security.read`, `settings.manage`; each widget gated separately | — |
| Users overview / All Users / User detail | `users.read` | Add `users.create`+`users.assign_role`; Edit `users.update`; Suspend/Activate/Unlock `users.suspend`; Roles tab edit `users.assign_role`; Revoke sessions `sessions.revoke` |
| Invitations | `invitations.read` | create/resend/revoke `invitations.manage` |
| Roles | `roles.read` | create/duplicate `roles.create`; edit/archive `roles.update`; change permissions `permissions.assign` |
| Permissions | `permissions.read` | — |
| Access Matrix | `permissions.read` | save `permissions.assign` |
| Sessions & Devices | authenticated | other users' list `sessions.read_all`; revoke others `sessions.revoke` |
| Audit Logs | `audit.read` | — |
| Security Events | `security.read` | acknowledge: `security.read` (candidate for its own `security.review`) |
| My Profile / Settings | authenticated | sensitive changes need recent reauth |
| System Settings | `settings.manage` | — |

## 3. Authorization invariants (each has a test — see test plan)
1. **Deny by default**; unknown permission/route ⇒ 403.
2. **No self-escalation / no escalation by delegation:** an actor can grant a role/permission/invitation role only if they hold **every** permission it contains (`grantableRoles`).
3. **Protected roles** (`super_admin`) cannot be edited, archived or have members removed such that no active Super Admin remains.
4. An admin cannot suspend/deactivate themselves or remove their own Super Admin role.
5. Removing `permissions.assign` from one's own non-super role via the matrix is rejected (no self-lockout from the feature).
6. Suspended/deactivated/pending users have **zero** effective permissions; archived roles grant nothing.
7. Permission changes apply on the **next request** of affected users (effective permissions computed per request, never embedded in the cookie).
8. Critical permissions (`users.assign_role`, `permissions.assign`, `settings.manage`) additionally require **recent reauthentication (≤ 5 min)** for the mutation that uses them *(proposed hardening; confirm with PO)*.
9. Denied attempts are written to the audit log with the missing permission.
10. **Hierarchy (added in slice 4):** you cannot act on a user whose effective permissions exceed yours (e.g. a User Admin cannot suspend or re-role a Super Admin). Refusals are audited as `denied`.
11. **Role hierarchy (slice 5):** you may only edit/rename/archive/matrix-change a role whose current permissions you hold; role permission changes need `permissions.assign` + recent reauth; a matrix save cannot remove your own `permissions.assign`.
12. Admins can never read passwords, MFA secrets, recovery codes, raw tokens or session tokens.

## 4. Change process
Permission catalog lives in code (`server/domain/permissions.ts`) and is seeded by migration. Adding a permission = code change + migration + matrix doc update + test. Roles are data and editable at runtime by authorised users.

## 5. GA Helpdesk permissions (R1 slices 1–2)
Keys are `resource.action` with exactly two lower-case parts (DB check `^[a-z_]+\.[a-z_]+$`). Super Admin receives every key automatically; **no other seed role holds any of them yet** — the GA system roles (Agent, Supervisor, Manager, Requester, …) arrive with the ticket slices.

| Module | Keys |
|---|---|
| Helpdesk setup | `locations.read` `locations.manage` · `teams.read` `teams.manage` · `calendars.read` `calendars.manage` |
| Service catalog | `catalog.read` (also defines "staff" for staff-only services) · `catalog.manage` (categories, services, publish/archive) · `forms.manage` (edit a service's form; deliberately separate from `catalog.manage`) |
| Tickets (slice 3) | `tickets.read_team` (queue: own teams, assigned to me) · `tickets.read_all` · `tickets.assign` (assign; confirm for the requester) · `tickets.transition` (work: start/hold/resume/resolve/cancel/reopen) · `tickets.update` (priority, subject, location; file on someone's behalf) · `tickets.comment_internal` (write and read internal notes) |
| SLA and rules (slice 4) | `sla.read` · `sla.manage` (policies) · `rules.read` · `rules.manage` (assignment rules). Supervisor and Manager read; **GA Manager** also manages. Without `tickets.read_team`/`tickets.read_all` nobody sees a ticket's SLA state. |
| Approvals (slice 5) | `approvals.read` · `approvals.manage` (set/remove a service's approval policy). Supervisor reads; **GA Manager** manages. **Deciding needs no permission**: it is checked per approval (you must be the named approver, and never the requester or creator). |
| Retention (slice 13) | `retention.manage` (**critical**: Super Admin only by default, not in GA Manager) — set the organisation's retention periods, place and release legal holds, preview and run the clean-up. Each of those writes needs a fresh password check and is audited. Reading *what is kept* (`GET /api/retention/notice`) needs no permission: every signed-in person may. Only holders see that a ticket is under hold and why. |

Work orders (R2 slice 3): `workorders.read` and `workorders.complete` (do the work given to you: start, hold, checklist, time, photos, complete) are held by **GA Agent**; **GA Supervisor** and **GA Manager** also hold `workorders.manage` (create, edit, assign, cancel, the ticket's resolve setting, checklist templates) and `workorders.costs` (see and record parts and costs — agents never see money). A work order is visible to people who can see its ticket and to its assignee, even one outside the ticket's team. Existing organisations add these to their tuned GA roles by hand.

Vendors (R2 slice 4): `vendors.read` (the vendor directory, contracts, documents and scorecard) is held by **GA Agent**, **GA Supervisor** and **GA Manager**; `vendors.manage` (add, edit, block, contracts, documents) by **GA Manager** only. Giving a work order to a vendor is part of `workorders.manage`; the scorecard's cost needs `workorders.costs`. Existing organisations add these to their tuned GA roles by hand.

Preventive maintenance (R2 slice 5): `pm.read` (plans, visits, compliance report) is held by **GA Agent** and up; `pm.manage` (create, change, pause, remove plans) by **GA Supervisor** and **GA Manager**. The visits' tickets and work orders follow the usual ticket and work-order rules (team, assignee). Existing organisations add these to their tuned GA roles by hand.

Knowledge base (R2 slice 7): everyone signed in reads published articles for everyone; **GA Agent** holds `kb.read` (staff-only articles) and `kb.write` (drafts, files, service links); **GA Supervisor** and **GA Manager** add `kb.publish` (publish, archive, categories). Existing organisations add these to their tuned GA roles by hand.

Cost and budget (R2 slice 8): **GA Supervisor** holds `cost.read` (the cost report and its export) and `budgets.read` (budgets and cost centres); **GA Manager** also holds `budgets.manage` (budgets, cost centres, currency and labour rate). The work order's own costs stay behind `workorders.costs`. Existing organisations add these to their tuned GA roles by hand.

Bookings (R3 slice 1): booking for yourself, and changing or cancelling your own bookings, needs no permission, like making a request (vendor accounts are kept out by the vendor portal gate). `bookings.read_all` (see who booked what and why) and `bookings.manage` (book for others; change or cancel anyone's booking, with a reason) by **GA Supervisor** and up; `resources.manage` (add, edit, archive resources and choose who may book them) by **GA Manager**. A resource limited to certain roles can still be booked by holders of `bookings.manage`. Existing organisations add these to their roles on the Access Matrix screen (doc 22). R3 slice 2: approving bookings is part of `bookings.manage`; checking in is for the person booked for (or `bookings.manage`); the calendar feed shows only the person's own bookings and needs no permission. R3 slice 3: a vehicle trip is handled by the people the booking is for or by and by its driver, or `bookings.manage` (which alone assigns drivers and sees the driver picker); vehicle documents and driving licences need `resources.manage`; the utilisation report needs `bookings.read_all`. R3 slice 4: registering your own visitors needs no permission; a new system role **Receptionist** holds `visitors.checkin` (walk-ins for any host, check-in and out) and `visitors.read_all` (every visit, the visitor report); **GA Supervisor** holds `visitors.read_all`; **GA Manager** also `visitors.checkin` and `visitors.manage` (block list and visitor rules). Existing organisations get the Receptionist role automatically (missing system roles are created at start-up); add the permissions to tuned GA roles on Access Matrix. R3 slice 5: everyone sees the mail logged for or by them; `mail.manage` (the mailroom) is held by **Receptionist** and **GA Manager**. R3 slice 6: `inventory.read` is held by **GA Agent** and up, `inventory.adjust` (corrections and stock-takes) by **GA Supervisor** and up, `inventory.manage` (items, receipts, issues, transfers) by **GA Manager**. R3 slice 7: `supplies.fulfil` (the supplies desk) is held by **GA Supervisor** and up; asking for supplies needs no permission, and the requester's manager approves without one. R3 slices 10 and 11 add no permission: the channel set-up uses `integrations.manage`, everyone manages their own channels, and field mode uses `workorders.complete`. R3 slice 9: `directory.manage` (group-to-role mappings, HR sync, SCIM through a key) is held by **User Admin** and **Super Admin**; roles given by a directory group still obey the grant rule (only roles the mapper could grant), and Super Admin is never mapped. R3 slice 8 adds no permission: exporting or scheduling a report needs that report's own permission (for the scheduler, and for every recipient at every run).

Integrations (R2 slice 10): `integrations.manage` (critical) — API keys and webhooks — is in no seeded GA role (Super Admin has every permission). An API key acts as its creator limited to its scopes, and only on `/api/v1`; the creator's own permissions still apply to every call.

Vendor portal (R2 slice 11): the seeded **Vendor** role holds `workorders.read` and `workorders.complete`. Its people are linked to one vendor on the vendor's page; their scope is that vendor's work orders, and a server-side allow-list keeps every other endpoint closed to them whatever their role grants. Linking refuses anyone with other rights.

Assets (R2 slice 1): `assets.read` (the register, an asset's trail and the requests about it — limited to those the reader may see in the queue) is held by **GA Agent** and **GA Supervisor**; `assets.manage` (add, edit, move, hand out, retire/restore, asset types) and `assets.import` (CSV import) by **GA Manager**; Super Admin has all. Choosing an asset on a ticket needs `tickets.update` (the picker endpoint), not the register. **Existing organisations keep their tuned roles**: add these permissions to GA Agent, GA Supervisor and GA Manager in *Roles* to adopt them.

Organisation e-mail wording (slice 16) uses `settings.manage` (critical): reading, previewing and saving it; saving needs a fresh password check.

Attachments (slice 7) have **no permission of their own** either: whoever can see a ticket can open and add files to it (until it is finished); the uploader or staff holding `tickets.update` can remove one; an upload for a form that is not submitted yet is visible only to its uploader.

Notification preferences (slice 11) need **no permission**: everybody manages only their own. What each person may switch off is limited by the catalog, not by roles: an approval request (`approval.needed`) is locked on in the app and by e-mail because the work stops without it.

Delegation and hand-over (slice 10) need **no permission of their own**: everybody manages their own out-of-office cover, an approver can hand over their own waiting approval, and `approvals.manage` can also hand over anyone's. Setting a person's manager needs `users.update` (and the usual rule that you cannot act on someone who holds permissions you do not). A cover can never decide a request they made: the rule that nobody approves their own request still holds for the person who decides *in place of* another.

Bulk actions and saved views (slice 9) have **no permission of their own**: they need queue access (`tickets.read_team` or `tickets.read_all`) and, per action, the permission of the single-ticket action — assigning needs `tickets.assign`, changing priority needs `tickets.update`, and start/hold/resume/resolve/cancel are decided per ticket exactly as a single transition is. Personal views are private to their owner; sharing a view needs `tickets.assign`, and a shared view can be changed or removed by its owner or by anyone holding `tickets.assign`.

The go-live checklist and quick start (slice 8) have **no permission of their own** either. `GET /api/setup/status` needs any one of `locations.manage`, `teams.manage`, `calendars.manage`, `catalog.manage`, `forms.manage`, `sla.manage`, `rules.manage`, `approvals.manage`; `POST /api/setup/quick-start` needs **all** of `locations.manage`, `teams.manage`, `sla.manage` and `rules.manage` (it creates a site, a team, SLA policies and a rule, so it must not be a way around those permissions).

Reports (slice 6) have **no permission of their own**: they use the ticket queue's visibility (`tickets.read_team` = own teams' tickets, `tickets.read_all` = everything), so a report can never show more than the queue.

System roles added in slice 3 (each organisation owns its copy): **GA Agent** = `tickets.read_team`, `tickets.transition`, `tickets.update`, `tickets.comment_internal`, `catalog.read` · **GA Supervisor** = Agent + `tickets.read_all`, `tickets.assign`, `teams.read` · **GA Manager** = Supervisor + `locations.read/manage`, `calendars.read/manage`, `teams.manage`, `catalog.manage`, `forms.manage`, `sla.manage`, `rules.manage`, `approvals.manage` (slice 8: the manager *runs* the helpdesk configuration, but holds no `users.*`, `roles.*`, `invitations.*` or audit permission — user administration stays with User Admin). **Existing organisations keep the role as they tuned it**: system roles other than the protected Super Admin are never overwritten on upgrade, so add the missing permissions to *GA Manager* in Roles (or create a new organisation) to adopt the change. The *User* role stays permission-less: **requesters need no permission** — creating, listing and commenting on their own tickets, and cancelling/confirming/reopening them, are open to any signed-in person and decided per ticket by the service.

The requester portal (`/api/portal/*`) needs only a signed-in session; what a person sees is decided by the service (published, live, audience), not by a role. Coverage: `AuthorizationTests` still fails any endpoint without `.RequirePermission()`, `.RequireAuthorization()` or `.AllowAnonymous()`.

**Module summaries (doc 53).** `GET /api/{work-orders,assets,vendors,maintenance}/summary` need the module's read permission (`workorders.read`, `assets.read`, `vendors.read`, `pm.read`); `GET /api/kb/summary` needs `kb.write`. Vendor spend in the vendor summary is shown only with `workorders.costs`. No new permissions.
