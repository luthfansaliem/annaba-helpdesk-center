# 08 — API Specification (Phase 3)

> **Update (ADR-013):** the backend is now **ASP.NET Core (.NET)**, not Next.js/TypeScript. Where this document mentions Next.js route handlers, Server Actions, Drizzle, zod or Vercel for the API, read the equivalent in `13-adr-dotnet-backend.md`. Design intent (layers, schema, contracts, security controls) is unchanged.

Status: **proposed — awaiting PO approval.** REST-style JSON over HTTPS. Same-origin only (no public CORS). Most mutations are also exposed as Server Actions that call the **same use-cases**; this document is the contract for both.

## 1. Conventions
- Base path `/api`. Authentication via `__Host-sid` cookie (HttpOnly, Secure, SameSite=Lax, Path=/). No bearer tokens, no tokens in URLs/logs.
- **CSRF:** all non-GET require `Origin` matching `APP_ORIGIN` and header `X-CSRF-Token` (value from `GET /api/auth/csrf`, bound to the session).
- **Idempotency:** `Idempotency-Key` header optional on create endpoints (24 h dedupe) — recommended for invitations/users.
- **Pagination:** `?page=1&pageSize=10` (max 100) → `{ data: [...], page, pageSize, total }`. **Sorting:** `?sort=name&dir=asc` (whitelisted columns).
- **Success:** `200/201` with JSON body. **No content:** `204`.
- **Error envelope** (never contains stack traces, SQL or token values):
```json
{ "error": { "code": "denied|validation|conflict|invariant|rate_limited|not_found|locked|expired|unauthenticated",
              "message": "Human readable", "fields": { "email": "Enter a valid email address." }, "requestId": "…" } }
```
| code | HTTP |
|---|---|
| unauthenticated | 401 |
| denied | 403 |
| locked (session locked) | 423 |
| not_found | 404 |
| validation | 422 |
| conflict | 409 |
| invariant (business rule, e.g. last Super Admin) | 409 |
| expired (token/session) | 410 / 401 |
| rate_limited | 429 + `Retry-After` |
- Every endpoint below lists its **required permission**. `self` = authenticated user acting on own resource (no permission). `public` = unauthenticated. Anything not listed is denied.
- Every mutation writes an audit event (action name in the last column).

## 2. Auth (AUTH-001…008)
| Method & path | Auth | Purpose | Body → Response | Audit / security event |
|---|---|---|---|---|
| POST `/auth/login` | public, rate-limited | email+password | `{email,password,remember,returnTo?}` → `{status:"ok"|"mfa_required"|"verify_email"|"suspended"|"deactivated"|"locked", next?}` (generic `401 invalid_credentials` on any credential failure; uniform timing via dummy hash) | `auth.login.success/failure`; `login.failed`, `account.locked` |
| POST `/auth/mfa/verify` | pre-auth session | TOTP or recovery code | `{code}` or `{recoveryCode}` → `{next}` | `auth.mfa.verify`; failures count toward lockout |
| POST `/auth/logout` | self | end current session | → 204 | `auth.logout` |
| POST `/auth/register` | public, rate-limited; **404 unless `registrationMode=open`** | self-registration | `{name,email,password}` → always `202 {message}` (same for existing email) | `auth.register` |
| POST `/auth/verify-email` | public (token) | consume token, activate | `{token}` → 200 / `410 expired` | `auth.email.verify` |
| POST `/auth/verify-email/resend` | public, rate-limited | new link | `{email}` → always 202 | `auth.email.resend` |
| POST `/auth/forgot-password` | public, rate-limited | request reset | `{email}` → always 202 | `auth.password.forgot` |
| POST `/auth/reset-password` | public (token) | set password | `{token,password}` → 200; revokes all sessions per policy | `auth.password.reset`; `password.reset` |
| POST `/auth/lock` | self | set `sessions.locked_at` | → 204 | `auth.lock` |
| POST `/auth/unlock` | locked session | reauth | `{password,code?}` → 200 (5 failures ⇒ session revoked) | `auth.unlock` |
| POST `/auth/reauth` | self | refresh `reauth_at` for sensitive ops | `{password,code?}` → 200 | `auth.reauth` |
| GET `/auth/csrf` | self | CSRF token | → `{token}` | — |
| GET `/auth/me` | self | current user + `effectivePermissions[]` (UI only) | → user | — |
| POST `/invitations/accept` | public (token) | accept | `{token,name,password}` → 200 | `invitation.accept` |
| GET `/invitations/lookup?token=` | public | validate token state | → `{state:"valid"|"expired"|"used"|"revoked", email?, roles?}` (reveals nothing for unknown tokens) | — |

## 3. Users (USR-001…005) — all require auth + permission
| Method & path | Permission | Notes |
|---|---|---|
| GET `/users` | `users.read` | filters `q,status,role,verified`; sort `name|status|lastLogin|createdAt` |
| GET `/users/stats` | `users.read` | totals, status distribution, growth series |
| POST `/users` | `users.create` **and** `users.assign_role` | `{name,email,phone?,department?,jobTitle?,roleIds[],mode?:"invite"}` — **invitation only** (the second "create active" mode was dropped in ADR slice 4); creates a credential-less pending placeholder + invitation; roles must be *grantable by actor*; needs recent reauth; audit `user.create` |
| GET `/users/{id}` | `users.read` | includes roles, effective permissions |
| PATCH `/users/{id}` | `users.update` | `{name?,phone?,department?,title?}` + `version` for optimistic lock |
| POST `/users/{id}/suspend` · `/deactivate` · `/activate` | `users.suspend` | invariants: not self, not last Super Admin; suspend/deactivate revokes sessions |
| POST `/users/{id}/unlock` | `users.suspend` | clears `locked_until`, resets counter |
| POST `/users/{id}/resend-verification` | `users.update` | rate-limited |
| PUT `/users/{id}/roles` | `users.assign_role` | `{roleIds[]}` atomic replace; escalation + last-admin guards |
| GET `/users/{id}/sessions` | `sessions.read_all` | no tokens |
| DELETE `/users/{id}/sessions` | `sessions.revoke` | revoke all (or `?sessionId=`) |
| GET `/users/{id}/activity` | `audit.read` | audit rows where actor/target is the user |
| POST `/users/bulk` | per-action permission | `{ids[],action:"suspend"|"resend_verification"}`; returns per-item results (partial success allowed, each audited) |

## 4. Invitations (INV-001…003)
| Method & path | Permission |
|---|---|
| GET `/invitations?status=` | `invitations.read` |
| POST `/invitations` | `invitations.manage` (+ roles grantable by actor) |
| POST `/invitations/{id}/resend` | `invitations.manage` (rotates token, extends expiry) |
| POST `/invitations/{id}/revoke` | `invitations.manage` |

## 5. Roles & permissions (ROLE/PERM)
| Method & path | Permission | Notes |
|---|---|---|
| GET `/roles` | `roles.read` | includes permission count + user count |
| GET `/roles/{id}` | `roles.read` | permissions + assigned users (paged) |
| POST `/roles` | `roles.create` (+`permissions.assign` if permissions given) | |
| PATCH `/roles/{id}` | `roles.update` (+`permissions.assign` if permissions change) | protected roles → `409 invariant` |
| POST `/roles/{id}/duplicate` | `roles.create` | |
| POST `/roles/{id}/archive` | `roles.update` | system roles / roles still assigned → `409 invariant` |
| GET `/permissions` | `permissions.read` | catalog (read-only) |
| GET `/access-matrix` | `permissions.read` | roles × permissions + `grantable[]` for actor |
| PUT `/access-matrix` | `permissions.assign` | `{ changes: [{roleId, add[], remove[]}] , expectedVersion }` (`expectedVersion` = opaque fingerprint string from GET) — **all-or-nothing** transaction; rejects adding permissions actor lacks; protected roles; own `permissions.assign` removal |

## 6. Sessions & devices (SES-001)
| Method & path | Auth |
|---|---|
| GET `/sessions/me` | self — current + others, labels only |
| DELETE `/sessions/me/{id}` | self |
| DELETE `/sessions/me?except=current` | self (revoke all others) |
| DELETE `/sessions/me` | self (sign out everywhere) |
| GET `/sessions` | `sessions.read_all` (paged) |

## 7. Monitoring
| Method & path | Permission |
|---|---|
| GET `/audit?q&outcome&category&from&to` | `audit.read` |
| GET `/audit/{id}` | `audit.read` |
| GET `/security-events?severity&ack` | `security.read` |
| POST `/security-events/{id}/ack` | `security.read` *(review is a read-level operational action; may become its own permission)* |
| GET `/dashboard/admin` | returns **only the widgets the caller may see**, each gated by its permission |
| GET `/dashboard/me` | self |

## 8. Profile & settings
| Method & path | Auth | Notes |
|---|---|---|
| GET/PATCH `/me/profile` | self | name, phone, department, title |
| PUT `/me/avatar` (raw bytes) · DELETE `/me/avatar` · GET `/avatars/{userId}` | self (view: owner or `users.read`) | PNG/JPEG/WebP ≤ 256 KB, 32–2048 px; **bytes inspected, not re-encoded** (slice 6 deviation); served with exact type + nosniff + sandbox CSP |
| POST `/me/password` `{newPassword}` | self + **reauth ≤5 min** (replaces a `currentPassword` field) | revokes other sessions |
| POST `/me/email` | self + reauth | sends confirmation to **new** address, notice to old; switch happens on confirmation |
| POST `/me/mfa/enroll` → `{otpauthUri, secret}` · POST `/me/mfa/confirm` `{code}` → `{recoveryCodes[10]}` (shown once) | self + reauth | |
| POST `/me/mfa/recovery-codes` | self + reauth | regenerate |
| POST `/me/mfa/disable` `{password, code\|recoveryCode}` | self (password + second factor in body) | 409 `invariant` when MFA policy is required |
| GET/PATCH `/me/preferences` | self | language, timezone, notifications, autoLock (≤ system idle) |
| GET `/settings` | `settings.manage` | secrets returned as `{set:true}` |
| PATCH `/settings` | `settings.manage` | validated; diff audited; fresh reauth. `PUT/DELETE /settings/email-api-key` write-only secret; `GET /settings/public` anonymous (registrationOpen, passwordMinLength, mfaPolicy). Email *provider* is deployment config (read-only) |

## 9. Rate limits (defaults; configurable in code)
| Scope | Key | Limit |
|---|---|---|
| login | IP | 20 / 5 min |
| login | account (email hash) | 5 failures → lock `lockoutMinutes` |
| mfa verify | session | 5 failures → revoke pre-auth session |
| forgot / resend / register | IP + email hash | 3 / 15 min each |
| unlock | session | 5 failures → revoke |
| admin bulk | user | 10 / min |

## 10. Security headers (all responses)
`Content-Security-Policy` (nonce-based, no inline scripts except nonce, `frame-ancestors 'none'`), `Strict-Transport-Security` (2 y, includeSubDomains), `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy` (deny camera/mic/geolocation), `Cache-Control: no-store` on authenticated pages and every `/api` response.

## 11. Versioning & compatibility
Unversioned internal API (same-origin, deployed with the UI). If a public API is introduced later it goes under `/api/v1` with token auth as a separate design.

## 12. GA Helpdesk (R1 slices 1–2)
Same envelope, CSRF, error codes and concurrency rules as above. `PATCH`/`PUT` bodies carry `version`; a stale version is `409`. Every mutation is audited. Cross-organisation ids answer `404`.

| Route | Permission | Notes |
|---|---|---|
| `GET /api/locations` `GET /api/locations/{id}` | `locations.read` | `?includeArchived` |
| `POST /api/locations` `PATCH …/{id}` `POST …/{id}/archive` `…/restore` | `locations.manage` | tree + code rules |
| `GET /api/calendars` `GET …/{id}` | `calendars.read` | |
| `POST /api/calendars` `PUT …/{id}` `POST …/{id}/default` `…/archive` `…/restore` | `calendars.manage` | windows/holidays replaced as a whole |
| `GET /api/teams` `GET …/{id}` | `teams.read` | |
| `POST /api/teams` `PATCH …/{id}` `PUT …/{id}/members` `POST …/archive` `…/restore` | `teams.manage` | member list is all-or-nothing |
| `GET /api/catalog/categories` `GET /api/catalog/services` `GET …/services/{id}` | `catalog.read` | services: `?includeArchived&categoryId&q` |
| `POST/PATCH …/categories`, `POST …/categories/{id}/archive` `…/restore` | `catalog.manage` | two levels, sibling-unique names |
| `POST/PATCH …/services`, `POST …/services/{id}/publish` `…/unpublish` `…/archive` `…/restore` | `catalog.manage` | archive also unpublishes |
| `GET …/services/{id}/form` `…/form/versions` `…/form/versions/{n}` | `catalog.read` | `n = 0` → empty form |
| `PUT …/services/{id}/form` | `forms.manage` | body `{ fields, note?, version }`; creates version N+1; identical form = no-op; `422` with `fields[i].prop` keys |
| `GET /api/portal/catalog?q=` | any signed-in user | published + live + audience-visible only |
| `GET /api/portal/services/{id}` | any signed-in user | service + current form; hidden = `404` |
| `POST /api/portal/services/{id}/check` | any signed-in user | body `{ answers }`; `422` per question, or `{ serviceId, formVersion, values }` (nothing is stored) |

### 12.1 Tickets (slice 3)
Requester routes need only a session; the service decides per ticket. A ticket the caller may not see is `404`. State changes take `version` (stale = `409`).

| Route | Who | Notes |
|---|---|---|
| `POST /api/portal/services/{id}/requests` | any signed-in | body `{ subject, description?, answers, locationId?, onBehalfOfUserId? }`; `422` keys `subject`, `description`, `locationId`, `answers.<key>`; on-behalf needs `tickets.update` |
| `GET /api/portal/locations` · `GET /api/portal/people?q=` | any signed-in | live places with their path · ≥ 2 chars, ≤ 20, name + department only |
| `GET /api/tickets/mine` | any signed-in | `?status&priority&q&page&pageSize` (`status=open` = not closed/cancelled). `q` matches number, subject, **description** and requester, as a literal substring (`%`, `_` and `\` are ordinary characters). Lists answer `{ data, page, pageSize, total, capped }`: counting stops at **10,000**, and `capped: true` means "more than `total`" |
| `GET /api/tickets` | `tickets.read_team` or `tickets.read_all` (else `403`) | the queue; filters as above plus `assigneeId`, `teamId`, `unassigned`, and (slice 9) `assignedToMe`, `serviceId`, `locationId` (a place **and everything inside it**), `from` / `to` (creation instant, `to` exclusive, ISO 8601) |
| `GET /api/tickets/{id}` | per ticket | detail with labelled answers, timeline (internal notes only with `tickets.comment_internal`), `allowed` actions, `canComment/canEdit/canAssign` |
| `POST /api/tickets/{id}/transition` | per ticket | `{ action: start\|hold\|resume\|resolve\|cancel\|confirm\|reopen, reason?, resolutionCode?, resolutionNote?, version? }`; illegal move `409`, not permitted `403`, missing reason/resolution `422` |
| `POST /api/tickets/{id}/comments` | per ticket | `{ body, internal }`; internal needs `tickets.comment_internal`; finished tickets `409` |
| `POST /api/tickets/{id}/assign` | `tickets.assign` | `{ assigneeId?, teamId?, version? }`; assignee must hold `tickets.transition`; same values = no-op |
| `GET /api/tickets/assignees?teamId=` | `tickets.assign` | active people who can work tickets (a team's members when `teamId` is given) |
| `PATCH /api/tickets/{id}` | `tickets.update` | `{ subject, priority, locationId, version }` |

### 12.2 SLA policies and assignment rules (slice 4)
Same envelope and concurrency rules (`version`; stale = `409`). `ids` in a reorder must list every active item exactly once (else `422`). `priority` is `low|medium|high|urgent` or empty.

| Route | Permission | Notes |
|---|---|---|
| `GET /api/sla/policies` | `sla.read` | `?includeArchived`; ordered by position |
| `POST /api/sla/policies` · `PATCH …/{id}` · `POST …/{id}/archive` · `…/restore` · `POST /api/sla/policies/reorder` | `sla.manage` | body `{ name, serviceId?, categoryId?, locationId?, priority?, calendarId?, firstResponseMinutes, resolutionMinutes, escalate1Percent?=50, escalate2Percent?=80, version? }` |
| `POST /api/sla/preview` | `sla.read` | `{ serviceId, locationId?, priority? }` → the policy a new ticket would get (or none) |
| `GET /api/assignment-rules` | `rules.read` | |
| `POST /api/assignment-rules` · `PATCH …/{id}` · `POST …/archive` · `…/restore` · `POST …/reorder` | `rules.manage` | body `{ name, serviceId?, categoryId?, locationId?, priority?, teamId, strategy: none\|round_robin\|least_loaded, version? }` (responses use `roundrobin`/`leastloaded`) |
| `POST /api/assignment-rules/preview` | `rules.read` | `{ serviceId, locationId?, priority? }` → `{ ruleName, teamName, strategy, assigneeName, candidates[{name, openTickets, lastAssignedAt}] }`; assigns nothing |

Tickets: the list takes `?sla=ontrack|atrisk|breached|paused|met|none` and `?sort=due`; list items and the detail carry `slaState` / `sla{…}` **for staff only** (requesters get `expectedBy` on the detail). Escalations appear as internal `escalation` entries on the timeline.

### 12.3 Approvals and notifications (slice 5)
Policy routes carry their own permission; the rest need only a session and are checked per person. The two `link` routes are **public by design** (the one-time token is the credential; unknown, used, expired and withdrawn tokens all answer `410`; rate-limited per address).

| Route | Who | Notes |
|---|---|---|
| `GET /api/approvals/policies` | `approvals.read` | every live service with its policy (steps resolved to names) |
| `PUT /api/approvals/policies/{serviceId}` · `POST …/remove` | `approvals.manage` | body `{ steps: [{ name, rule: any\|all, userIds, roleIds, requesterManager? }], fallbackUserId?, version? }` (a step needs at least one person, role or `requesterManager`); `422` keys `steps[i].…`; identical policy = no-op |
| `GET /api/approvals/mine` · `GET …/mine/count` | signed in | my pending and decided approvals (each may carry `onBehalfOfName`) · `{ pending }` |
| `POST /api/approvals/{id}/decide` | the approver | `{ approve, comment? }` (comment required to reject); `404` if it is not yours, `409` if already decided or no longer needed |
| `GET /api/approvals/link/{token}` · `POST …/link/{token}` | **anonymous** | summary (reads only) · `{ approve, comment? }` |
| `GET /api/notifications?unread&page&pageSize` · `GET …/unread-count` | signed in | only my own |
| `POST /api/notifications/{id}/read` · `POST …/read-all` | signed in | someone else's id is `404` |
| `GET /api/notifications/preferences` | signed in | `{ emailMaster, items[{ kind, group, locked, inApp, email, defaultEmail, custom }] }` for every kind of notification (`ticket.*`, `sla.*`, `approval.*`); `emailMaster` is the profile's e-mail switch |
| `PUT /api/notifications/preferences` | signed in | body `{ items: [{ kind, inApp, email }] }` (1–50, each kind once); a switch equal to the default is stored as "as usual". `422` for an unknown kind, a duplicate, or switching off a **locked** kind (`approval.needed`: `items[i].kind`) |
| `POST /api/notifications/preferences/reset` | signed in | removes all of the person's choices; returns the defaults |

Tickets: `status` can now be `pendingapproval` or `rejected`; the detail carries `approvals[]` (step, approver, status, comment, time, via) and `myApprovalId`; the timeline has `approval` entries.

### 12.3a Delegation, hand-over and managers (slice 10)
| Route | Who | Notes |
|---|---|---|
| `GET /api/approvals/delegations` | signed in | my last 30 delegations: `{ id, delegateId, delegateName, startsAt, endsAt, note, state: upcoming\|active\|ended\|revoked }` |
| `POST /api/approvals/delegations` | signed in | body `{ delegateId, startsAt?, endsAt, note? }` → `201 { delegation, handedOver, kept }`. `422` keys `delegateId` (not yourself, an active person), `endsAt` (required, after the start, in the future, ≤ 180 days), `startsAt` (not more than a day in the past; no overlap with another of your live delegations), `note` (≤ 300). If it has already started, approvals waiting for you move to the cover at once (`handedOver`); those that cannot (the cover made the request, or is already an approver of that step) stay (`kept`) |
| `POST /api/approvals/delegations/{id}/end` | the owner | ends it now (`state: revoked`); someone else's is `404`, an already ended or finished one `409`. Approvals already moved stay with the cover |
| `POST /api/approvals/{id}/reassign` | the approver, or `approvals.manage` | body `{ toUserId }`; only a **pending** approval (`409` otherwise); `404` if it is not yours and you cannot manage; `422 toUserId` for the requester or creator, someone already approving that step, or an inactive person. The new approver gets a fresh one-time link and the old link stops working |
| `PUT /api/users/{id}/manager` | `users.update` | body `{ managerId \| null }`; `422 managerId` for yourself, an inactive person, or a reporting line that would loop. `GET /api/users/{id}` now carries `managerId` and `managerName` |

Approval rows and the ticket's `approvals[]` carry `onBehalfOfName` when the person decides in somebody else's place.

### 12.4 Reports (slice 6)
Read-only; any signed-in session may call them, but a person without `tickets.read_team` or `tickets.read_all` gets `403`, and everyone sees only the tickets the ticket queue would show them. `days` is clamped to 1–90 (default 30).

| Route | Response |
|---|---|
| `GET /api/reports/operations?days=` | `{ days, from, to, kpis{ open, overdue, atRisk, pendingApproval, unassigned, created, resolved, avgFirstResponseMinutes, avgResolutionMinutes, slaAttainmentPercent }, daily[{day, created, resolved}], byStatus[], byPriority[], backlogByTeam[], aging[], attention[] }` |
| `GET /api/reports/sla?days=` | `{ days, from, to, met, breached, attainmentPercent, openBreached, openAtRisk, byPriority[], byTeam[], byService[], breaches[{ id, number, … reason: first_response\|resolution }] }` (averages and percentages are `null` when there is no data) |

### 12.5 Attachments (slice 7)
Files are never public: every route needs a session and, for a ticket's files, the right to see that ticket (otherwise `404`, like a missing ticket). Uploads are `multipart/form-data` with one part named `file`; the request is capped at 10 MB, the file at 8 MB. The CSRF header is still required (`DisableAntiforgery` only switches off the framework's own form token).

| Route | Who | Notes |
|---|---|---|
| `POST /api/uploads` | any signed-in | a file for a request form that is not submitted yet → `201 { id, fileName, contentType, size, inline }`; at most 30 unclaimed files per person; unclaimed ones are deleted after 24 h |
| `POST /api/tickets/{id}/attachments` | anyone who can see the ticket | `201` the attachment item; finished tickets (closed, cancelled, rejected) `409`; at most 20 files per ticket |
| `GET /api/tickets/{id}/attachments/{attachmentId}` | anyone who can see the ticket | the bytes, with our own `Content-Type` (from the allow-list, never the sender's), `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; sandbox`, `Content-Disposition` **inline for images only**, attachment otherwise |
| `DELETE /api/tickets/{id}/attachments/{attachmentId}` | the uploader, or staff holding `tickets.update` | `204`; the bytes are deleted, the row is kept (soft delete) for the trail; other people `403`; finished tickets `409` |

Refusals are `422` with `error.fields.file`: type not on the allow-list (`.jpg .jpeg .png .gif .webp .pdf .doc .docx .xls .xlsx .ppt .pptx .txt .csv`), content that does not match the type (a program renamed `.png`), empty file, larger than 8 MB, rejected by the scanner, or too many files. The ticket detail carries `attachments[{ id, fileName, contentType, size, inline, uploadedById, uploadedBy, createdAt, canDelete }]` and `canAttach`. A `file` question's answer is the list of upload ids (`["<guid>", …]`, at most the question's `max`, default 5, never more than 10); the ticket claims them when it is created (someone else's, unknown or already-claimed ids → `422 answers.<key>`).

### 12.6 Setup checklist and quick start (slice 8)
Signed-in only; permissions are checked by the service (see `09-rbac-matrix.md`): anyone else gets `403`.

| Route | Who | Notes |
|---|---|---|
| `GET /api/setup/status` | any setup-manage permission | `{ steps[{ key, done, required, count, link }], ready, done, total }`. Required steps: `site`, `calendar`, `team` (a team with at least one member), `catalog` (a published service), `sla` (a live policy), `routing` (a live rule). Advice only: `approvals`, `scanner` (a real virus scanner is configured), `test` (a ticket exists). `ready` = every required step done |
| `POST /api/setup/quick-start` | all of `locations.manage`, `teams.manage`, `sla.manage`, `rules.manage` | body `{ siteName?, teamName?, memberIds?[], leadId? }` → `{ created[], skipped[], status }`. Creates only what is missing, in one transaction: first site, first team (or fills an existing empty one) with the chosen people, the four starter SLA policies if none are live, and an "All requests" rule (least loaded) to that team. `422` keys `siteName`, `teamName`, `memberIds`, `leadId` only for the parts that are missing; running it again creates nothing |

### 12.7 Queue productivity: bulk actions and saved views (slice 9)
| Route | Who | Notes |
|---|---|---|
| `POST /api/tickets/bulk` | queue readers (`403` otherwise); the action's own permission | body `{ ids[1..100], action: assign\|priority\|start\|hold\|resume\|resolve\|cancel, assigneeId?, teamId?, priority?, reason?, resolutionCode?, resolutionNote? }` → `{ action, requested, succeeded, items[{ id, number, ok, error }] }`. `assign` needs `tickets.assign`, `priority` needs `tickets.update` (`403` for everything); transitions are judged per ticket like a single transition. The whole body is validated first (`422` keys `ids`, `action`, `assigneeId`, `priority`, `reason`, `resolutionNote`) so nothing changes on a bad request; after that each ticket is applied on its own through the single-ticket methods, and a ticket that cannot take the change (wrong status, not allowed) is reported in `items` while the rest go through. A ticket the person cannot see is reported as `"Ticket not found."`, exactly like an unknown id |
| `GET /api/ticket-views` | queue readers | my views plus shared ones: `[{ id, name, filter{}, shared, mine, ownerName, canEdit }]` |
| `POST /api/ticket-views` | queue readers; `shared: true` needs `tickets.assign` | body `{ name (2–60), filter{}, shared? }`. The filter takes only the queue's own names (`q`, `status`, `priority`, `sla`, `sort`, `assigneeId`, `teamId`, `locationId`, `serviceId`, `unassigned`, `assignedToMe`, `from`, `to` as `yyyy-MM-dd`); blank and `false` values are dropped, anything unknown or malformed is `422` (`filter`). Names are unique per person (ignoring case) and among shared views; at most 30 per person, 50 shared |
| `PATCH /api/ticket-views/{id}` · `DELETE …/{id}` | the owner, or staff holding `tickets.assign` for a shared view | the filter is **replaced**; someone else's private view is `404`; only the owner can change whether it is shared |

### 12.8 Data retention, legal holds and privacy (slice 13)
| Route | Who | Notes |
|---|---|---|
| `GET /api/retention/notice` | any signed-in person | what their organisation keeps and for how long: `{ enabled, finishedTicketDays, ticketAction, attachmentDays, notificationDays, auditDays }` |
| `GET /api/retention` | `retention.manage` | the policy: the same plus `holds` (count), `updatedAt`, `lastRun { at, tickets, files, notifications, filesFailed }`. An organisation that never saved one reads as off |
| `PUT /api/retention` | `retention.manage` + a fresh password check | full replace: `{ enabled, finishedTicketDays?, ticketAction: anonymise\|delete, attachmentDays?, notificationDays? }`; `null` = keep. `422` keys `finishedTicketDays` (30–3650), `attachmentDays` (7–3650 and not longer than the ticket period), `notificationDays` (7–3650), `ticketAction`, `enabled` (on with no period). Audited (`retention.policy.update`, with before/after) and raises a security event |
| `POST /api/retention/preview` | `retention.manage` | same body; validates like `PUT` and returns what a run would do now, changing nothing: `{ tickets, heldTickets, files, fileBytes, notifications }` |
| `POST /api/retention/run` | `retention.manage` + a fresh password check | runs the saved policy for this organisation now (`409` while it is off): `{ tickets, files, notifications, more, filesFailed }`. Every manual run is audited (`retention.run`, counts only) |
| `GET /api/retention/holds?page&pageSize` | `retention.manage` | `{ data[{ id, ticketId, number, subject, status, reason, placedBy, placedAt }], … }` |
| `PUT /api/tickets/{id}/legal-hold` · `DELETE …` | `retention.manage` + a fresh password check | body `{ reason (3–300) }`. `404` unknown ticket, `409` already held / not held. Audited (`retention.hold.place` / `release`). While held a ticket is skipped by every clean-up and its files cannot be removed by hand (`409`) |
| `GET /api/retention/files` | `retention.manage` | compares this organisation's live attachment rows with the bytes on the volume: `{ files, missing, missingSample[≤50 { attachmentId, fileName, ticketId, number }], orphans, orphanBytes, checkedAt }`. Orphans are bytes with no live row and older than one hour |
| `POST /api/retention/files/clean` | `retention.manage` + a fresh password check | removes those orphans → `{ removed, bytes, failed }`; audited (`files.orphans.clean`); a file the volume refuses is counted in `failed` |

The ticket detail carries `anonymisedAt` (set once the policy removed what people wrote; the ticket then answers `409` to every change) and, for people with `retention.manage` only, `canHold` and `legalHold { reason, placedAt, placedBy }`. `GET /api/me/export` now also returns `helpdesk { requestsTotal, requestsIncluded, requests[…], comments[…], files[…] }`: the requests the person is the requester of (subject, description, answers, status, dates, resolution), their own public comments and the files they attached (names only), never internal notes or other people's words.

### 12.9 Organisation e-mail wording (slice 16)
| Route | Who | Notes |
|---|---|---|
| `GET /api/settings/email-wording` | `settings.manage` | `{ senderName, subjectPrefix, footerEn, footerId, updatedAt }`; all `null` = the product's wording |
| `PUT /api/settings/email-wording` | `settings.manage` + a fresh password check | full replace, same body without `updatedAt`; blank = none. `422` keys `senderName` (one line, ≤ 60), `subjectPrefix` (one line, ≤ 30), `footerEn` / `footerId` (≤ 400 characters, ≤ 6 lines); no control characters; an unchanged save is `422`. Audited (`settings.email_wording.update`) |
| `POST /api/settings/email-wording/preview` | `settings.manage` | same body → `{ versions[{ lang: en\|id, subject, text }] }` for a sample resolved-request mail; nothing is saved or sent |

Applied to every message the organisation sends when it is queued, after it is put in the recipient's language: `<prefix> <subject>`, the closing text in that language (the other one if only one is set) before the sign-off, and `— <senderName>` as the sign-off. HTML-escaped in the HTML part.

### 12.10 Asset register (R2 slice 1)
| Route | Who | Notes |
|---|---|---|
| `GET /api/asset-types?includeArchived` | `assets.read` | `[{ id, name, description, fields[FormField], archived, assets (in service), version }]` |
| `POST /api/asset-types` · `PUT …/{id}` · `POST …/{id}/archive` · `…/restore` | `assets.manage` | body `{ name (2–100, unique among active, ignoring case), description?, fields[] (form definition, ≤ 20; types text, long_text, number, date, select, yes_no only), version? }`; `422` keys `name`, `fields[i].…`. An archived type is not offered for new assets |
| `GET /api/assets?q&typeId&status&locationId&assignedToId&warrantyWithinDays&page&pageSize` | `assets.read` | default = in service (not retired); `status` = `all`, `in_use`, `in_repair`, `stored`, `retired`; `q` = literal text in tag, name or serial; `locationId` includes the places below it; `warrantyWithinDays` = warranty ends within N days (or already ended). Items `{ id, tag, name, typeId, typeName, status, serial, locationPath, assignedToName, warrantyEnd, updatedAt }` |
| `GET /api/assets/{id}` | `assets.read` | everything above plus `fields` (the type's questions), `values`, `events[]` (newest first), `tickets[≤50]` and `ticketCount` (**only requests the reader may see in the queue**), `retiredAt`, `retireReason`, `canManage` |
| `POST /api/assets` · `PUT /api/assets/{id}` | `assets.manage` | body `{ tag?, name, typeId, serial?, status (in_use\|in_repair\|stored), locationId?, assignedToId?, department?, purchaseDate?, purchaseCost?, warrantyEnd?, vendorName?, notes?, fields{}, version? }`. No tag → `AST-000001`… (per organisation); tags are stored in capitals and unique. `422` keys per field and `fields.<key>`; `409` on a stale `version` or a retired asset |
| `POST /api/assets/{id}/retire` · `…/restore` | `assets.manage` | retire body `{ reason (3–300), version? }`; restoring makes it `stored` |
| `GET /api/assets/pick?q` | `tickets.update` | ≤ 20 active assets for the ticket's asset picker `[{ id, tag, name, typeName, locationPath }]` |
| `GET /api/assets/export?…filters` | `assets.read` | CSV (UTF-8 with BOM), columns `tag,name,type,serial,status,location_code,assigned_to_email,department,purchase_date,purchase_cost,warranty_end,vendor,notes`, plus `x_<key>` per question when one `typeId` is chosen; ≤ 20,000 rows (`X-Export-Truncated`); formula cells neutralised; audited |
| `POST /api/assets/import` | `assets.import` | body `{ csv, dryRun }` (≤ 2 MB, ≤ 5,000 rows) → `{ rows, creates, updates, problems[{ line, tag, errors{} }], applied }`. A tag that exists updates that asset (an empty cell keeps the value); other lines create. All-or-nothing: written only when every line is correct and `dryRun` is false. `422 csv` for an unreadable file, unknown or duplicate columns, missing `name`/`type` |

| `GET /api/assets/card?tag=…` or `?id=…` | any signed-in person | (R2-2) what a scanned label points to: `{ id, tag, name, typeName, locationId, locationPath, status, retired, canOpen }`; the tag in any case; `404` for another organisation's asset or an unknown tag. `canOpen` = the reader has `assets.read` |
| `GET /api/assets/labels?ids=a,b,…` or `?typeId&locationId&status&q` | `assets.read` | (R2-2) `[{ id, tag, name, typeName, locationPath }]` in tag order, at most 500, for a printable sheet; each QR encodes `<origin>/a/<tag>` |

R2-2 also adds `frequent=true` to the list and export (assets with 3 or more requests in the last 365 days), and `requestsLastYear` and `repairHint` (`frequent` \| `frequent_out_of_warranty` \| null) to the asset detail. A request made through the portal may carry `assetId` (any asset of the organisation that is not retired; `422 assetId` otherwise).

Tickets: `PATCH /api/tickets/{id}` takes `assetId` (replaced as given, like `locationId`; a retired asset is `422 assetId`), and the ticket detail carries `asset { id, tag, name, retired }`.

### 12.11 Work orders and checklists (R2 slice 3)
Everything needs `workorders.read`; a work order is visible to someone who can see its ticket, and to the person it is given to. "Doing" means `workorders.complete` and being the person it is given to (or nobody is, and starting it takes it), or holding `workorders.manage`.

| Route | Who | Notes |
|---|---|---|
| `GET /api/tickets/{id}/work-orders` | ticket visible | `{ resolveTicket, canManage, items[{ id, number, title, status, required, assigneeId, assigneeName, teamName, ticketId, ticketNumber, ticketSubject, dueAt, tasksDone, tasksTotal, updatedAt }] }` |
| `POST /api/tickets/{id}/work-orders` | `workorders.manage` | body `{ title (3–150), instructions?, assigneeId?, teamId?, dueAt?, required (default true), requireAfterPhoto, tasks[{ text, required }], templateId?, resolveTicket? }`; numbered `<ticket>-W<n>`; the ticket's asset is copied; `409` on a finished ticket; ≤ 50 per ticket, ≤ 50 lines |
| `PUT /api/tickets/{id}/work-orders/settings` | `workorders.manage` | `{ resolveTicket }`: resolve the ticket when its last required work order is done |
| `GET /api/work-orders?mine&status&page&pageSize` | `workorders.read` | `status` = `open` (default: not finished), `all`, `new`, `in_progress`, `on_hold`, `done`, `cancelled`; soonest due first |
| `GET /api/work-orders/{id}` | visible | detail with `tasks`, `times`, `totalMinutes`, `costs` and `costTotal` (**null without `workorders.costs`**), `files`, `allowed` actions, `canEdit`, `canWork`, `canCosts`, `missing` (what still blocks completing) |
| `PUT /api/work-orders/{id}` | `workorders.manage` | same body as create (`tasks[].id` keeps a line and its tick); `409` stale version or finished |
| `POST /api/work-orders/{id}/action` | doing (cancel: `workorders.manage`) | `{ action: start\|hold\|resume\|complete\|cancel, reason?, note?, version? }`. Hold and cancel need a reason (3–500); complete needs a note and `422 complete` lists what is missing (required lines, the after photo). An impossible move is `409`. Any change on a closed, cancelled or rejected ticket is `409` |
| `POST /api/work-orders/{id}/tasks/{taskId}` | doing, work order in progress | `{ done, note? }` |
| `POST /api/work-orders/{id}/time` · `DELETE …/time/{timeId}` | doing (delete: own line or manager) | `{ minutes (1–1440), date? (≤ 90 days back, not future), note? }` |
| `POST /api/work-orders/{id}/costs` · `DELETE …/costs/{costId}` | `workorders.costs` | `{ kind: part\|labour\|service\|other, description (2–200), quantity (> 0, 2 decimals), unitCost (≥ 0, 2 decimals) }`; not on a cancelled work order |
| `POST /api/work-orders/{id}/files?stage=before\|after` (multipart `file`) · `GET …/files/{fileId}` | doing · visible | photo evidence, stored with the ticket's files (same checks and limits) and served with the same sandboxing headers |
| `GET/POST /api/checklist-templates` · `PUT …/{id}` · `POST …/{id}/archive` · `…/restore` | read · `workorders.manage` | `{ name (2–100, unique among active), items[{ text (2–300), required }] (1–50) }` |

Tickets: resolving is `409` while a **required** work order is open; cancelling a ticket cancels its open work orders ("The ticket was cancelled."); with `resolveTicket`, completing the last required work order resolves the ticket as the system (code `fixed`, note "All required work orders are done (…)", audit `ticket.auto_resolve`).

### 12.12 Vendors and contracts (R2 slice 4)
Reading needs `vendors.read`; every change needs `vendors.manage`. Validation errors are `422` with field keys (`contacts[i].name`, `locationIds`, …).

| Route | Who | Notes |
|---|---|---|
| `GET /api/vendors?q&status&locationId&page&pageSize` | `vendors.read` | `status` = `active` (default), `blocked`, `all`; `q` matches name or services; `locationId` = vendors covering that place directly or through a place above it. Items `{ id, name, services, phone, email, blocked, sites, openWorkOrders, nextContractEnd }` |
| `GET /api/vendors/pick?q` | `workorders.manage` | active (not blocked) vendors for the work order form, ≤ 50 |
| `GET /api/vendors/{id}` | `vendors.read` | detail with `contacts`, `sites[{ locationId, path }]`, `contracts[{ …, state: active\|ended\|upcoming }]`, `documents`, `score { workOrders, done, open, onTime, late, cost }` (**cost is null without `workorders.costs`**), `workOrders` (latest), `canManage`, `version` |
| `POST /api/vendors` · `PUT /api/vendors/{id}` | `vendors.manage` | `{ name (2–150), services?, email?, phone?, website? (http/https), address?, taxId?, notes?, contacts[{ name, role?, email?, phone? }] (≤ 20), locationIds[] (active places, ≤ 200), version? }`; contacts and places are replaced; `409` stale version |
| `POST /api/vendors/{id}/block` · `…/unblock` | `vendors.manage` | block needs `{ reason (3–300) }`. A blocked vendor can't be given **new** work orders (`422 vendorId`); work it already has is unchanged |
| `POST /api/vendors/{id}/contracts` · `PUT/DELETE …/contracts/{contractId}` | `vendors.manage` | `{ title (2–150), reference? (≤ 60), startsOn, endsOn? (≥ startsOn), value? (≥ 0, 2 decimals), notes? }`; changing the end date resets its reminder |
| `POST /api/vendors/{id}/documents?contractId` (multipart `file`) · `GET/DELETE …/documents/{docId}` | `vendors.manage` · `vendors.read` · `vendors.manage` | same file checks as ticket files (types, 8 MB, virus scan), ≤ 50 per vendor; served with the sandboxing download headers |

Work orders: `POST/PUT` bodies take `vendorId?` and `purchaseOrder?` (≤ 50); list items carry `vendorName`, the detail `vendorId`, `vendorName`, `vendorPhone`, `purchaseOrder`.

Contract reminders (daily worker): 30 days before `endsOn` and again on/after it (ended more than 30 days ago: silent), to users whose roles hold `vendors.manage` (≤ 20), notification kind `vendor.contract` (in-app and e-mail; a digest when more than 5 are due at once).

### 12.13 Preventive maintenance (R2 slice 5)
Reading needs `pm.read`; changing needs `pm.manage`. Dates are the organisation's dates (the default business calendar's time zone, Asia/Jakarta when there is none).

| Route | Who | Notes |
|---|---|---|
| `GET /api/maintenance/plans?q&status&assetId&page&pageSize` | `pm.read` | `status` = `all` (default), `active`, `paused`; `assetId` = plans for that asset directly or through its type. Items `{ id, name, target, unit, every, nextDueOn, leadDays, teamName, assigneeName, vendorName, paused, openVisits, overdueVisits }` |
| `GET /api/maintenance/plans/{id}` | `pm.read` | the plan with `upcoming` (next 5 dates), `targets` (how many assets it covers now), the latest 50 `visits[{ dueOn, asset…, locationName, ticketId, ticketNumber, workOrderId, workOrderNumber, state, completedAt }]`, `lastYear` (compliance counts), `canManage`, `version` |
| `POST /api/maintenance/plans` · `PUT …/{id}` | `pm.manage` | `{ name (3–120, unique), instructions? (≤ 4000), serviceId (not archived; may be unpublished), assetId \| assetTypeId (+ locationId?) \| locationId, unit: day\|week\|month\|year, every (1–365 / 52 / 24 / 5), startsOn, endsOn?, leadDays (0–90, default 7), checklistTemplateId?, teamId?, assigneeId?, vendorId? (not blocked), requireAfterPhoto, version? }`. A new or changed rhythm starts from today (earlier dates are not missed). Work already within the lead time is made before the answer |
| `POST /api/maintenance/plans/{id}/pause` · `…/resume` | `pm.manage` | resuming continues from today: dates passed while paused are skipped, not missed |
| `DELETE /api/maintenance/plans/{id}` | `pm.manage` | `204`; `409` once the plan has visits (pause it instead) |
| `GET /api/maintenance/compliance?from&to&by` | `pm.read` | visits due between the dates (default: the last 90 days; at most a year) grouped `by` = `plan` (default), `team`, `vendor`, `site` (top-level place): `rows[{ key, label, planned, onTime, late, open, overdue, missed, cancelled, percent }]` + `total`. `percent` = on time ÷ (planned − not yet due); null when nothing is due |

Visit states: `open` (not yet due), `overdue`, `on_time` / `late` (work order completed by / after the end of the due date), `cancelled`, `missed` (overtaken while the scheduler caught up; no ticket), `removed` (the ticket was deleted).

The scheduler (with the daily reminders, and right after a plan is saved or resumed) makes, for every date within the lead time, one ticket per target on the plan's service — filed for the plan's owner, no form, no approval, **no SLA clock** — with one required work order (the plan's name, instructions, checklist, person, team and vendor; due at the end of the date). Each occurrence is claimed by moving the plan's counter with a conditional update in the same transaction as its visits. Dates already overtaken by the next date are recorded as `missed` (audited `pm.catch_up`; at most 500 rows per run). A blocked vendor or an inactive person is left off the work order. A failure is audited as `pm.generate` (failure) and retried next run.

Work orders (change in this slice): completing the last required work order now resolves a ticket that is still *submitted*, *assigned* or *reopened* too (it is started on the way), not only one in progress.

### 12.14 Satisfaction (R2 slice 6)
| Route | Who | Notes |
|---|---|---|
| `PUT /api/tickets/{id}/rating` | the ticket's requester | `{ score (1–5), comment? (≤ 1000) }`; only when the ticket is *resolved* or *closed* and at most 14 days after it was resolved (`409` otherwise, and always for planned-maintenance tickets); staff get `404`/`403`. Changing a rating updates it; a timeline line is added ("Rated 4 out of 5." / "Changed the rating from 2 to 4 out of 5."). A score of 1–2 notifies the team's leads (`ticket.low_rating`, in-app and e-mail) once per ticket — or, when the team has no lead, up to 20 people with `tickets.read_all` |
| `GET /api/tickets/{id}` | as before | now includes `rating { score, comment, at, canRate, rateUntil }` |
| `GET /api/reports/csat?days&by` | `tickets.read_team` | `days` 1–365 (default 30), `by` = `team` (default), `agent`, `site`, `vendor` (vendors of the ticket's work orders; "In-house" otherwise). `{ ownOnly, count, average, satisfiedPercent (4–5), low (1–2), distribution[1..5], rows[{ key, label, count, average, satisfiedPercent, low }], comments (latest 20), trendUnit ("week" up to 90 days, else "month"), trend[{ period, count, satisfied, low, average, satisfiedPercent }] }` (empty weeks or months are kept). People without `tickets.read_all` see only ratings of tickets they were working (`ownOnly: true`) |

The vendor scorecard (`GET /api/vendors/{id}`) now carries `score.rating` (average requester rating of tickets with work by the vendor) and `score.ratings`. The "resolved" e-mail asks for a rating.

### 12.15 Knowledge base (R2 slice 7)
Everyone signed in reads **published** articles meant for everyone; `kb.read` also reads staff-only ones; `kb.write` writes; `kb.publish` publishes, archives and manages categories. Articles a person may not read answer `404`.

| Route | Who | Notes |
|---|---|---|
| `GET /api/kb/categories` · `POST` · `PUT …/{id}` · `POST …/{id}/archive` · `…/restore` | signed in · `kb.publish` | `{ name (2–60, unique among active) }` |
| `GET /api/kb/articles?q&categoryId&status&page&pageSize` | signed in | readers: published and readable, `q` = every word in title, summary or text. Writers may pass `status` = `published` (default), `draft` (never published or with unpublished changes), `archived`, `review` (review date passed), `all`; they also get `views`, `helpful`, `notHelpful`, `deflections`, `reviewDue` |
| `GET /api/kb/articles/{id}?version` | readable / writers | readers get the published version (counted as a view); writers may ask any `version` (`0` = latest) and get `versions[]`. `{ title, summary, body (Markdown), category…, visibility, status, publishedVersion, latestVersion, shownVersion, hasDraft, reviewBy, reviewDue, services[], files[], myFeedback, canWrite, canPublish, version }` |
| `POST /api/kb/articles` · `PUT …/{id}` | `kb.write` | `{ title (3–150), summary? (≤ 300), body (1–50,000), categoryId?, visibility: everyone\|staff, reviewBy?, serviceIds[] (replaced as given), note?, version? }`. A text change makes a new version (a draft until published); category, visibility, review date and services apply at once; `409` stale version |
| `POST …/{id}/publish?version` · `…/archive` · `…/restore` | `kb.publish` | publish copies the latest (or given) version into what readers see |
| `POST …/{id}/feedback` | readable | `{ helpful }`, one answer per person (changeable) |
| `GET /api/kb/suggest?q&serviceId` | signed in | up to 8 readable published articles: the service's own first (`forService`), then text matches (`q` ≥ 3 characters) |
| `POST …/{id}/solved` | readable | the person read a suggestion and did not file the request (deflection); `204` |
| `POST …/{id}/files` (multipart `file`) · `GET …/files/{fileId}` · `DELETE …` | `kb.write` · readable · `kb.write` | same checks as ticket files, ≤ 20 per article; images inline, others as downloads, with the sandboxing headers. In the text, `![alt](file:<id>)` shows an image |

Markdown is rendered by the web app into elements (never raw HTML): headings, lists, quotes, code, emphasis, links to `https:`, `http:`, `mailto:` and site paths only, and images of the article's own files.

### 12.16 Cost and budget (R2 slice 8)
| Route | Who | Notes |
|---|---|---|
| `GET /api/costs/settings` · `PUT` | signed in · `budgets.manage` | `{ currency (3 letters, default IDR), labourRatePerHour? (≥ 0, 2 decimals; null = time has no cost) }` |
| `GET /api/costs/report?from&to&by` · `GET /api/costs/report.csv?…` | `cost.read` | costs between the dates (default: the last 12 months; at most two years), `by` = `month` (default), `site`, `cost_center`, `category`, `service`, `department` (the requester's), `vendor`, `asset`. Rows `{ key, label, part, labour, service, invoice, other, time (labour from logged time), total, workOrders }` + `total`, `currency`, `labourRatePerHour`. The CSV (UTF-8 with BOM) defuses formula-looking text |
| `GET /api/costs/centers` · `POST` · `PUT …/{id}` · `POST …/{id}/archive` · `…/restore` | `budgets.read` · `budgets.manage` | `{ code (1–20 of A–Z 0–9 . _ -, stored upper case, unique), name (2–100), locationIds[] (≤ 500; a place belongs to one cost centre), version? }` |
| `GET /api/costs/budgets?year` | `budgets.read` | `{ year, currency, canManage, items[{ scope, scopeId, scopeName, year, amount, spent, percent, state: ok\|near (≥ 80 %)\|over (≥ 100 %), note, version }] }` |
| `POST /api/costs/budgets` · `PUT …/{id}` · `DELETE …/{id}` | `budgets.manage` | `{ scope: cost_center\|site\|category, scopeId (an active cost centre, a top-level place, a service category), year, amount (> 0, 2 decimals), note?, version? }`; one budget per target and year; changing the amount or target resets its alerts |

Costs are the work orders' cost lines (`part`, `labour`, `service`, `invoice` — a vendor's invoice — and `other`; quantity × unit cost) dated by when they were recorded, plus logged time × the labour rate ÷ 60 dated by the work date. They are attributed through the work order's ticket: its place (site = top-level place; cost centre = the nearest place up the tree that is mapped to one), service and category, the requester's department, and the work order's vendor and asset. Work orders now also return `labourCost` (with `workorders.costs`, when a rate is set), and their cost totals use the same rounding.

Budget alerts (daily worker): at 80 % and at 100 % of a budget of the current year, the people with `budgets.manage` (≤ 20) get `budget.threshold` (in-app and e-mail), once per level (claimed with a conditional update).

### 12.17 E-mail to ticket (R2 slice 9)
| Route | Who | Notes |
|---|---|---|
| `POST /api/inbound/email/{token}` | the mail service (no session, no Origin; the secret token authenticates) | body = the raw RFC 5322 message (`message/rfc822` or any type), ≤ 10 MB (`413` above). `200 { outcome: created\|replied\|ignored\|rejected\|duplicate, reason, ticketNumber }`; `404` unknown token or mailbox off; `422` not an e-mail with a sender; `429` more than 500 messages an hour. Goes through the web front like every `/api` call |
| `GET /api/settings/inbound-email` · `PUT` | `settings.manage` | `{ address, defaultServiceId (a published service), enabled, version? }`; the detail adds `tokenHint` and the last 50 `recent` messages with their outcome. Switching on needs a token first (`409`) |
| `POST /api/settings/inbound-email/token` | `settings.manage` + recent password re-entry | a new secret path `/api/inbound/email/<token>`, shown once; the old one stops working. Only its SHA-256 is stored |

Rules: only active people of the organisation are heard (sender = sign-in e-mail, case-insensitive); auto-replies (`Auto-Submitted`, `Precedence: bulk/junk/list`, `List-Id`, `X-Autoreply`), bounces and no-reply senders are ignored; the same Message-ID is a `duplicate`; at most 20 messages per sender per hour. A subject containing a ticket number the sender may see adds the **new text** of the reply (above "On … wrote:", "-----Original Message-----", "From:" headers, quoted `>` lines and the `-- ` signature) as a public comment, marked "(by e-mail)"; a finished ticket refuses it and tells the sender in the app. Anything else becomes a request on the mailbox's service as the sender (subject without "Re:/Fwd:", text as description, ≤ 4000 characters). Attachments (≤ 10) are stored with the usual file checks; refused ones are listed. Server-to-server endpoints are marked so the Origin check does not apply to them (they carry no session).

### 12.18 API keys and webhooks (R2 slice 10)
| Route | Who | Notes |
|---|---|---|
| `GET /api/integrations/keys` · `POST` · `POST …/{id}/revoke` | `integrations.manage` (+ recent password re-entry to create) | `{ name (3–80), scopes[] (tickets.read, tickets.write, catalog.read), expiresOn? }` → `{ key, token }` — the token once; ≤ 50 active keys |
| `GET /api/integrations/webhooks` · `POST` · `PUT …/{id}` · `DELETE …/{id}` · `GET …/{id}/deliveries` | `integrations.manage` (+ re-entry to create or change the address) | `{ url (https, public, port 443/8443), description?, events[], enabled, version? }` → `{ webhook, secret }` on create — the secret once; ≤ 20 webhooks; deliveries = the last 50 |
| `/api/v1/…` | an API key with the scope | see the public API reference in `50-ga-helpdesk-r2-slice10-report.md`; 120 requests/minute per key |

Webhook events: `ticket.created`, `ticket.status_changed` (`extra: { from, to, action }`), `ticket.assigned` (`extra: { assigneeId, teamId }`), `ticket.commented` (public comments only; `extra: { body, author, authorId }`), `workorder.completed` (`extra: { workOrder }`). Body `{ id (event id), type, createdAt, data: { ticket: { id, number, subject, status, priority, serviceId, serviceName, requesterId, assigneeId, teamId, locationId, assetId, createdAt, updatedAt }, extra } }`; headers `X-Annaba-Event`, `X-Annaba-Delivery`, `X-Annaba-Timestamp`, `X-Annaba-Signature: v1=…`.

### 12.19 Vendor portal (R2 slice 11)
| Route | Who | Notes |
|---|---|---|
| `POST /api/vendors/{id}/portal-users` · `DELETE …/portal-users/{userId}` | `vendors.manage` | `{ email }` of an existing person whose rights are only the Vendor role's; one vendor per person. `GET /api/vendors/{id}` lists `portalUsers` for vendor managers (null for others) |
| `GET /api/auth/me` | signed in | now carries `vendorName` for a vendor's person |

A vendor's person reaches only `/api/auth`, `/api/me`, `/api/sessions/me`, `/api/notifications`, `/api/work-orders`, `/api/settings/public` and `/api/avatars`; anything else is `403` ("Vendor accounts can only see their company's work orders."). On work orders their scope is the vendor's work orders: they list, read and work them (start/hold/resume, checklist, photos, time, complete) without becoming the assignee; `mine=true` means their company's work.

### 12.21 Bookings (R3 slice 1, doc 56)
| Route | Who | Notes |
|---|---|---|
| `GET /api/resources?kind&locationId&q&capacity&feature&archived` | signed in | Active resources (`archived=true` adds archived ones for `resources.manage`), a place includes everything below it, at most 200. Item: `{ id, kind, name, code, description, locationId, place, capacity, features[], openFrom, openUntil (HH:mm or null), openDays (bit mask, Mon = 1), bufferMinutes, maxMinutes, needsApproval, allowedRoleIds[], archived, timeZone, canBook, version }` |
| `GET /api/resources/{id}` | signed in | 404 for an archived resource unless `resources.manage` |
| `POST /api/resources` · `PUT /api/resources/{id}` | `resources.manage` | Body as the item (without id, place, timeZone, canBook). 422 per field; a stale `version` or duplicate code is 409/422 |
| `POST /api/resources/{id}/archive` · `…/restore` | `resources.manage` | Archiving a resource with upcoming bookings is 409 |
| `GET /api/bookings/availability?from&to&kind&locationId&capacity&feature&resourceId` | signed in | At most 31 days. `{ from, to, rows[{ resource, bookings[], freeNow }] }`: live bookings overlapping the window (buffer included) |
| `GET /api/bookings?scope=mine|all&from&to&resourceId&cancelled&page&pageSize` | signed in (`all`: `bookings.read_all` or `bookings.manage`) | `mine` = booked for or by me. `{ data, page, pageSize, total }` |
| `GET /api/bookings/{id}` | signed in | 404 unless it is mine or I hold `bookings.read_all`/`bookings.manage` |
| `POST /api/bookings` · `PUT /api/bookings/{id}` | signed in | `{ resourceId, startsAt, endsAt, title, attendees?, note?, bookedForId?, version? }`. 422: past (15-minute grace), more than 365 days ahead, not on 5-minute steps, longer than the resource allows (default 14 days), outside opening hours or days, more people than it holds, a role-restricted or archived resource, `bookedForId` other than me without `bookings.manage`. 409: the slot is taken (message names the clashing time, never who or why), or a concurrent booking won (constraint or deadlock) |
| `POST /api/bookings/{id}/cancel` | signed in | `{ reason? }`; someone else's booking needs `bookings.manage` and a reason; the person is notified (`booking.changed`) |
| `GET /api/bookings/summary` | signed in | `{ resources, bookingsToday, inUseNow, freeNow, myUpcoming, utilisationPercent (rooms and vehicles, booked ÷ opening hours, last 7 days), byKind, next7Days[{ day, bookings }], busiest }` |

Booking for yourself needs no permission, like making a request; a vendor's person never reaches these routes (vendor portal gate, §12.19). A booking item hides `title`, `bookedFor`, `note` and `attendees` (null) from people who are neither involved nor hold `bookings.read_all`/`bookings.manage`: others see only that the time is taken.

### 12.22 Booking lifecycle (R3 slice 2, doc 57)
| Route | Who | Notes |
|---|---|---|
| `POST /api/bookings/series` | signed in | `{ resourceId, startsAt, endsAt, title, attendees?, note?, every: day|weekday|week, until?: date, count?: 2–52, bookedForId? }`. Every occurrence keeps the local start time in the resource's zone. Occurrences that clash or break a rule are skipped: `{ seriesId, made[], skipped[{ startsAt, reason }] }`. 422 for a bad first occurrence or more than 52; 409 when none can be booked |
| `POST /api/bookings/{id}/approve` · `…/reject` | `bookings.manage` | `{ note? }` (a rejection needs one). Only a pending booking; the person (and whoever booked for them) is told (`booking.decided`) |
| `POST /api/bookings/{id}/check-in` | signed in (own) or `bookings.manage` | Open from 15 minutes before the start until the resource's check-in limit; 409 outside it, twice, or when the resource needs none |
| `POST /api/bookings/{id}/cancel` | as in §12.21 | `scope`: `this` (default) or `following` (this and the later live bookings of its series) |
| `GET /api/bookings?scope=pending` | `bookings.manage` | Bookings waiting for approval |
| `GET/POST/DELETE /api/bookings/feed` | signed in | State of my calendar link; make a new one (`{ url }` shown once; the old one stops working); turn it off |
| `GET /api/calendar/{token}.ics` | **anonymous** (the secret token is the credential) | `text/calendar`: the person's pending and confirmed bookings from 30 days ago to 180 days ahead (RFC 5545, escaped and folded). Unknown, retired or turned-off tokens and inactive accounts get 404 |

A booking item adds `status` pending, rejected or released, `seriesId`, `repeat`, `checkedInAt`, `canCheckIn`, `canDecide`, `decisionNote`, `needsCheckIn`; a resource adds `checkInMinutes`; the summary adds `pendingApprovals` (for `bookings.manage`) and `noShows7`. A new booking (or a move by its owner) on a resource that needs approval is pending unless the person holds `bookings.manage`; approvers are told (`booking.approval`, at most 20 people).

Background (every minute, `BookingWorker`): a reminder 30 minutes before a confirmed booking (`booking.reminder`, once); release of a confirmed booking nobody checked in to within the resource's limit (status `released`, the person is told); a booking still pending when it starts is refused ("Not approved before it started"). Each item is claimed in one UPDATE.

### 12.23 Vehicles and utilisation (R3 slice 3, doc 58)
| Route | Who | Notes |
|---|---|---|
| `GET /api/bookings/{id}/trip` | the people booked for or by, the driver, or `bookings.manage` | 409 for a booking that is not of a vehicle. `{ …, needsDriver, driverId, driver, driverLicence, destination, purpose, odometerOut/In, pickedUpAt, returnedAt, distance, fuelCost, tollCost, parkingCost, cost, damageNotes, checklist[{item, ok}], canEdit, canAssignDriver, canPickUp, canReturn, lastOdometer, version }` |
| `PUT /api/bookings/{id}/trip` | same | `{ destination?, purpose?, needsDriver?, driverId?, version? }`. Only `bookings.manage` assigns someone else; a driver whose recorded licence expired before the start is refused (422). Asking for a driver alerts managers (`vehicle.alert`); the assigned driver is told. Final once returned (409) |
| `POST /api/bookings/{id}/trip/pick-up` | same | `{ odometerOut }`: from 30 minutes before the start until the end, not below the vehicle's last return reading; counts as check-in |
| `POST /api/bookings/{id}/trip/return` | same | `{ odometerIn, fuelCost?, tollCost?, parkingCost?, damageNotes?, checklist: { fuel, clean, no_new_damage, keys, documents } }`: every item answered; damage described when `no_new_damage` is false (managers alerted). Returning early ends the booking at the next 5-minute step |
| `GET/POST /api/resources/{id}/documents` · `PUT/DELETE …/{docId}` | `resources.manage` | `{ kind: registration|insurance|inspection|service|other, number?, expiresOn, note? }`; items carry `state` ok, soon (30 days) or expired |
| `GET /api/drivers/licences` · `POST /api/drivers/licences` · `PUT/DELETE /api/drivers/{userId}/licence` | `resources.manage` | `POST` takes the driver's `email` (an active person) with `number`, `class?`, `expiresOn` |
| `GET /api/drivers` | `bookings.manage` | People with a licence on record, valid ones first (the driver picker) |
| `GET /api/reports/utilisation?days&kind` | `bookings.read_all` | 7–365 days (default 30). Per resource: `openHours`, `bookedHours` (confirmed bookings inside opening hours), `percent`, `bookings`, `noShows`, `cancelled`, `trips`, `km`, `tripCost`; and a `total` row |

The cost report (§12.16) adds `vehicle` to every row: fuel, toll and parking of trips returned in the period, on the day of return, at the vehicle's place, for the person booked for; grouped by category or service they show as "Vehicle trips". They count in `total` but not in `workOrders`; the CSV gains a `vehicle trips` column. A daily run reminds `resources.manage` holders of documents and licences 30 days before and on expiry (`resource.document`), and the driver of their own licence.

### 12.24 Visitors (R3 slice 4, doc 59)
| Route | Who | Notes |
|---|---|---|
| `GET /api/visits?scope=mine|day|all&date&locationId&status&q&page&pageSize` | signed in (`day`: `visitors.checkin` or `visitors.read_all`; `all`: `visitors.read_all`) | `mine` = visitors I host or registered; `day` = expected on that date (organisation's time zone) or on site now. `q` matches name, company, plate |
| `GET /api/visits/{id}` | host, registrant, reception or read-all | Item: visitor details, host, place, window, `status` (expected, checked_in, checked_out, cancelled, no_show), `flag` and `idLast4` (reception and read-all only), badge, times, `anonymised`, `canChange`, `canReceive` |
| `POST /api/visits` · `PUT /api/visits/{id}` | signed in | `{ visitorName, company?, email?, phone?, vehiclePlate?, hostId?, hostEmail?, locationId?, purpose?, expectedFrom, expectedUntil, version? }`. Only `visitors.checkin` registers for another host (by id or e-mail). At most 7 days; not in the past; a year ahead at most. A name on the block list is flagged and `visitors.manage` holders are told (`visitor.flagged`) |
| `POST /api/visits/{id}/cancel` | host, registrant or reception | Expected visits only |
| `POST /api/visits/{id}/check-in` | `visitors.checkin` | `{ badgeNumber, idLast4?, acknowledged? }`. Refused (409) while the visitor matches an active block-list entry; the ID digits and the acknowledgement are required when the settings ask; a badge already with a visitor on site is refused. The host is told (`visitor.arrived`) |
| `POST /api/visits/{id}/check-out` | `visitors.checkin` | |
| `GET /api/visits/summary` | `visitors.checkin` or `visitors.read_all` | `{ expectedToday, onSiteNow, checkedOutToday, noShows30, flagged, visitors30, perDay[30], bySite }` |
| `GET /api/visitors/settings` · `PUT` | signed in · `visitors.manage` | `{ acknowledgement, askIdLast4, retentionDays (7–3650) }` |
| `GET/POST /api/visitors/blocks` · `DELETE …/{id}` | `visitors.manage` | `{ name, company?, reason }`; adding or removing re-flags the expected visits |

The hourly scheduler turns expected visits whose window has passed into no-shows and blanks the personal details of finished visits older than the retention period (audit `visit.housekeeping`).

### 12.25 Mail and courier (R3 slice 5, doc 60)
| Route | Who | Notes |
|---|---|---|
| `GET /api/mail?scope=mine|desk&direction&status&q&page&pageSize` | signed in (`desk`: `mail.manage`) | `mine` = logged for or by me (voided hidden). `status`: waiting, collected, registered, sent, voided or `open`; `q` matches the person, the other party or the waybill |
| `POST /api/mail` | `mail.manage` | `{ direction, kind?, personEmail | personId, otherParty?, address?, carrier?, waybill?, description?, locationId? }`; an outgoing item needs the addressee. Incoming: the person is told at once (`mail.arrived`) |
| `POST /api/mail/{id}/collect` | `mail.manage` | `{ collectedBy? (defaults to the person), note? }`, incoming items waiting only |
| `POST /api/mail/{id}/send` | `mail.manage` | `{ carrier, waybill, note? }`, outgoing items not yet sent; the sender is told (`mail.sent`) |
| `POST /api/mail/{id}/void` | `mail.manage` | `{ note }` required; only items still at reception |
| `GET /api/mail/summary` | `mail.manage` | `{ waiting, waitingOver2Days, receivedToday, collectedToday, outgoingPending, sentThisWeek, oldestWaitingDays, received30[30], byCarrier }` |

The hourly scheduler reminds the person once when incoming mail has waited two days, and blanks the other party, address, waybill, description, collector and note of finished items older than the visitor retention period (audit `mail.housekeeping`).

### 12.26 Inventory (R3 slice 6, doc 61)
| Route | Who | Notes |
|---|---|---|
| `GET /api/inventory/items?q&category&locationId&low&archived` | `inventory.read` | Items with `total`, `low` and `stock[{ locationId, place, quantity, low }]` (a place filter keeps only that place's stock) |
| `GET /api/inventory/items/{id}` | `inventory.read` | |
| `POST /api/inventory/items` · `PUT …/{id}` · `POST …/{id}/archive` · `…/restore` | `inventory.manage` | `{ sku, name, category, unit, reorderLevel, reorderQuantity?, unitCost?, description?, version? }`. An item with stock cannot be archived (409) |
| `POST /api/inventory/items/{id}/moves` | `inventory.read` plus, by kind: receipt, issue, transfer → `inventory.manage`; adjustment → `inventory.adjust` | `{ kind, locationId, quantity, unitCost?, reference?, toLocationId? }`. Adjustments are signed and need a reason. Not enough stock → 409 ("Only 3 ream of … left at this place"). A receipt's unit cost becomes the item's. Returns the ledger lines written |
| `GET /api/inventory/movements?itemId&locationId&page&pageSize` | `inventory.read` | Newest first |
| `GET /api/inventory/reorder` | `inventory.read` | Item and place pairs at or below the reorder level, with `suggested` (the reorder quantity, else twice the level minus what is left) and its `cost` |
| `GET /api/inventory/summary` | `inventory.read` | `{ items, lowLines, stockValue, issued30, received30, byCategory, topIssued, issued30Days }` |
| `GET /api/inventory/stock-takes` · `GET …/{id}` | `inventory.read` | The last 20 counts with lines (`expected`, `counted`, `variance`) |
| `POST /api/inventory/stock-takes` · `PUT …/{id}/counts` · `POST …/{id}/close` | `inventory.adjust` | `{ locationId, note? }`; one open count per place. Counts `{ counts: { itemId: n } }`. Closing posts a Count movement for every counted difference |

When a balance falls to the reorder level, `inventory.manage` holders are told once (`stock.low`); the alert re-arms when the balance rises above the level again.

### 12.27 Supply requests (R3 slice 7, doc 62)
| Route | Who | Notes |
|---|---|---|
| `GET /api/supplies/catalog?locationId&q` | signed in | Active items with `available` at the place (the default place when none is given) and `unitCost` |
| `GET /api/supplies/requests?scope&status&page&pageSize` | signed in; `scope=desk` needs `supplies.fulfil` | `scope`: `mine` (default), `approvals` (waiting for me) or `desk`. `status`: `active` or one status. Each request has `lines[{ quantity, issued, backOrder, available }]` and `canCancel`, `canDecide`, `canFulfil` |
| `GET /api/supplies/requests/{id}` | the requester, the approver or `supplies.fulfil` | Others get 404 |
| `POST /api/supplies/requests` | signed in | `{ locationId?, note?, lines: [{ itemId, quantity }] }` (1–30 items, 1–10 000 each). Pending when the estimate reaches the approval amount, else open |
| `POST …/{id}/cancel` | the requester | Only while nothing was issued (409) |
| `POST …/{id}/decide` | the approver (the manager, or `supplies.fulfil` when there is none) | `{ approve, note }`; a rejection needs a note |
| `POST …/{id}/fulfil` | `supplies.fulfil` | `{ lines: { itemId: quantity } }`, at most what is outstanding (422). Not enough stock → 409 and nothing is issued. Partly issued keeps the rest as back-order |
| `POST …/{id}/close` | `supplies.fulfil` | `{ note }` (required). Closed when something was issued, else rejected |
| `GET /api/supplies/summary` | `supplies.fulfil` | `{ pending, open, partlyFulfilled, fulfilled30, issued30Cost, topItems }` |
| `GET /api/supplies/settings` · `PUT` | signed in · `inventory.manage` | `{ approvalAbove, defaultLocationId }` |
| `POST /api/work-orders/{id}/parts` | `workorders.costs` (not vendor accounts) | INV-005. `{ itemId, locationId, quantity }`: issues from stock and adds a part cost line at the item's cost. Deleting that cost line returns the stock |

Notifications: `supply.new` (the desk), `supply.approval` (the manager), `supply.update` (the requester).

### 12.28 Report exports and schedules (R3 slice 8, doc 63)
| Route | Who | Notes |
|---|---|---|
| `GET /api/reports/catalog` | signed in | The reports the caller may see: `[{ key, label, permission, description, defaultDays }]` |
| `GET /api/reports/export?report&days&format` | the report's permission | `format` csv (default) or xlsx; `days` 1–365. A file download (`no-store`) with `X-Report-Rows` and, when cut at 5 000 rows, `X-Report-Truncated: true`. Audited |
| `GET /api/report-schedules` | signed in | The caller's schedules (`audit.read`: everyone's), each with `recipients[{ name, email, canSee }]`, `nextRunAt`, `lastResult` |
| `POST /api/report-schedules` · `PUT …/{id}` | the owner | `{ name, report, format, frequency, weekday, monthDay, hour, days, recipients: [e-mail], active, version? }`. The owner and every recipient must be able to see the report (422 names who cannot). At most 30 schedules per owner |
| `DELETE …/{id}` · `POST …/{id}/send` | the owner | Send now keeps the regular time |

Every file is built with the recipient's own permissions; recipients who lost access are skipped, and a schedule whose owner lost access is paused.

### 12.29 Directory: groups, HR sync and SCIM (R3 slice 9, doc 64)
| Route | Who | Notes |
|---|---|---|
| `GET /api/directory/overview` | `directory.manage` | `{ ssoGroupsClaim, mappings, scimGroups, scimUsers, lastScimAt, lastSyncAt }` |
| `GET /api/directory/mappings` · `POST` · `DELETE …/{id}` | `directory.manage` (+ recent re-auth to change) | `{ group, roleId }`. Super Admin is refused (422); a role the caller cannot grant is refused (403) |
| `POST /api/directory/sync` | `directory.manage` (+ re-auth to apply) | `{ csv, dryRun, suspendMissing }` → `{ rows, updated, suspended, unchanged, notFound, failed, details[{ line, email, action, detail }] }` |
| `POST /api/v1/directory/sync` | API key, scope `directory.write`, owner holds `directory.manage` | The same, for a nightly job (no re-auth) |
| `/api/v1/scim/v2/ServiceProviderConfig` · `/Users` · `/Users/{id}` · `/Groups` · `/Groups/{id}` | API key, scope `directory.write` | SCIM 2.0: GET (filter `attr eq "value"`, `startIndex`, `count` ≤ 200), POST, PUT, PATCH, DELETE. Bodies and errors are `application/scim+json` |

SSO: the OIDC client reads `Sso__GroupsClaim` (default `groups`); at each sign-in the person's directory-managed roles follow their mapped groups.

### 12.30 Notification channels (R3 slice 10, doc 65)
| Route | Who | Notes |
|---|---|---|
| `GET /api/channels/settings` · `PUT …/{kind}` | `integrations.manage` | `{ enabled, settings: {…}, secrets: {…} }` (an empty secret keeps the stored one). Answers `{ kind, enabled, settings, secretsSet, people, failed7, updatedAt }`; secrets are never returned. Turning on without what the channel needs → 422 per missing field |
| `GET /api/channels/deliveries?kind` | `integrations.manage` | The last 100: channel, purpose, notification kind, person, attempts, delivered/failed, last error, `fellBack`; no message text |
| `GET /api/me/channels` | signed in | `{ available, mine[{ kind, hint, verified, enabled, replaceEmail, lastError, codePending }] }` |
| `POST /api/me/channels/{kind}` | signed in | WhatsApp `{ address: phone }` sends a code; Slack `{}` finds the person by e-mail; Teams `{ address: Workflows URL }` |
| `POST /api/me/channels/whatsapp/verify` | signed in | `{ code }`, 10 minutes, 5 tries |
| `PUT /api/me/channels/{kind}` · `DELETE` · `POST …/{kind}/test` | signed in | `{ enabled, replaceEmail }` |

### 12.31 Field mode (R3 slice 11, doc 67, ADR-GA-07)
| Route | Who | Notes |
|---|---|---|
| `GET /api/work-orders/offline` | `workorders.complete` | `{ takenAt, items }`: at most 50 of the caller's own open work orders, as on the work-order page, with costs left out |
| Any `POST`/`PUT`/`DELETE` under `/api/work-orders` with `Idempotency-Key` | as the route | A key already used successfully by the same person answers 200 with the work order as it is now and the header `Idempotent-Replayed: true`, without repeating the change. Keys: ≤ 100 characters, letters, digits and `- _ : .` (else 422); kept 7 days |

### 12.20 Module summaries (UI review, doc 52; periods, doc 54)
Read-only numbers for the analytics above each module list. Each uses the module's read permission and the same scope as its list; every count is done by the database. Work orders, vendors and maintenance take `?days=` (7–365, clamped; the response echoes it as `days`): it sets the window for finished work, compliance, ratings and spend. What is open, overdue or due soon is always "now". Assets and the knowledge base are snapshots and take no period.
| Route | Who | Returns |
|---|---|---|
| `GET /api/work-orders/summary?days` | `workorders.read` | `days` default 30. Over the work orders the person may see (a vendor's person: their company's): `open`, `overdue`, `dueThisWeek`, `done`, `onTime`, `late` (finished in the period), `avgCompletionHours` (finished in the period, from creation), `inHouseOpen`, `vendorOpen`, `byStatus` (open, in_progress, on_hold), `weekly` `{week, created, done}` (12 weeks, or ⌈days/7⌉ up to 53 for a longer period), `byVendor` (top 6 open), `byCategory` (top 10 service categories by average time, slowest first: `{key, label, done, avgHours}`) |
| `GET /api/assets/summary` | `assets.read` | `active`, `retired`, `warrantyExpired`, `warranty30`, `warranty90`, `unplaced`, `value` (purchase cost in service), `pmDue30`, `byStatus`, `byType` (top 8), `bySite` (top 8, a place counts for its site), `mostTickets` (top 5 in 12 months) |
| `GET /api/vendors/summary?days` | `vendors.read` | `days` default 365. `active`, `blocked`, `contractsActive`, `contractsExpiring60`, `openWorkOrders`, `overdueWorkOrders`, `spend` (cost lines added in the period), `top[8]` `{id, name, workOrders, done, open, onTime, late, spend, rating, ratings}`: open work whenever it started, finished work and ratings only from the period. Money (`spend`) is `null` without `workorders.costs` |
| `GET /api/maintenance/summary?days` | `pm.read` | `days` default 90. `active`, `paused`, `due7`, `due30`, `overdueVisits`, `period` (visits due in the period) and `monthly` (6 months, up to 12 for a longer period) as compliance rows (planned, onTime, late, open, overdue, missed, cancelled, percent), `upcoming[6]` `{id, name, nextDueOn}` |
| `GET /api/kb/summary` | `kb.write` | `published`, `drafts`, `archived`, `reviewDue`, `views`, `deflections`, `helpful`, `notHelpful`, `top[5]` `{id, title, views, deflections, helpfulPercent}`, `byCategory` (published) |

Form field model (`FormField`): `key` (`^[a-z][a-z0-9_]{0,39}$`, unique), `type` (`text` `long_text` `number` `date` `datetime` `select` `multi_select` `yes_no` `location` `user` `file`), `label`, `help?`, `required?`, `options?[{value,label}]`, `min?` `max?` (number), `maxLength?` (text), `pattern?` (short text), `visibleWhen?{field, op: equals|not_equals|in|filled|empty, values?}` — a condition may only look at an earlier question.
