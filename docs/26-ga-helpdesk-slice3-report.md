# 26 — GA Helpdesk, R1 slice 3: ticket lifecycle

Status: **implemented and verified** (API against PostgreSQL, full browser journey and accessibility in Chromium). Part of release R1 (see `23-ga-helpdesk-prd.md`). Requirements covered: **TKT-001** (ticket fields, per-organisation numbering), **TKT-002** (server-enforced lifecycle, audited), **TKT-003** (manual assignment only — rules come later), **TKT-004** (public comments and internal notes; no @mentions, e-mail replies or edit history yet), **TKT-008** (resolution code + notes, confirm/reopen, auto-close), **TKT-011** (queue list with filters and paging; no saved views, board or bulk actions yet), **TKT-015** (search by number, subject, requester), **CAT-004** (requester side: browse, fill, submit, "my requests").

## What was built
| Area | Where |
|---|---|
| Entities `Ticket`, `TicketEvent` (timeline), `TicketCounter` | `api/src/AnnabaDigital.Domain/Entities/Tickets.cs` |
| **State machine** (pure): legal moves, who may make them, assignment outcome, constants | `api/src/AnnabaDigital.Domain/TicketWorkflow.cs` |
| `TicketService`: create, list (mine / queue), detail, transition, assign, update, comment, automatic closing | `api/src/AnnabaDigital.Application/Helpdesk/TicketService.cs` |
| Portal helpers: places (with paths) and a minimal people lookup | `PortalCatalogService` in `ServiceFormService.cs` |
| Endpoints `/api/tickets/*`, `POST /api/portal/services/{id}/requests`, `/api/portal/locations`, `/api/portal/people` | `Endpoints/TicketEndpoints.cs`, `CatalogEndpoints.cs` |
| Tables `tickets`, `ticket_events`, `ticket_counters`; migration `Tickets` | `AnnabaDigitalDbContext.cs`, `Persistence/Migrations/*_Tickets.cs` |
| Permissions `tickets.read_team`, `tickets.read_all`, `tickets.assign`, `tickets.transition`, `tickets.update`, `tickets.comment_internal` | `Permissions.cs` |
| System roles **GA Agent**, **GA Supervisor**, **GA Manager** (every organisation gets its own copy; the existing *User* role is the requester) | `SystemRoles` in `Permissions.cs` |
| Background worker closing unconfirmed resolutions | `TicketAutoCloseWorker` (same on/off switches as the review scheduler) |
| UI: Requests → New request / My requests; Helpdesk → Ticket Queue; ticket page with actions, assign, edit, activity, comments (EN + ID) | `src/app/(app)/requests/`, `src/app/(app)/tickets/`, `src/components/tickets/`, `src/components/forms/dynamic-form.tsx` (adds a search-based person picker) |

## The lifecycle
`Submitted → Assigned → InProgress ⇄ OnHold → Resolved → Closed`, plus `Cancelled` and `Reopened` (approval states join with the approvals slice). The pure table `TicketWorkflow.Next` is the only place moves are defined; a test checks the **whole** status × action matrix.

| Action | From | To | Who |
|---|---|---|---|
| start | Submitted, Assigned, Reopened | InProgress | staff working it (assignee, a supervisor, or anyone if unassigned) |
| hold (reason) / resume | Assigned, InProgress / OnHold | OnHold / InProgress | same |
| resolve (code + note) | InProgress | Resolved | same |
| cancel (reason) | Submitted, Assigned, Reopened (requester); also OnHold (staff) | Cancelled | requester until work starts; staff working it |
| confirm | Resolved | Closed | the requester, or a supervisor on their behalf; otherwise automatic after 3 days |
| reopen (reason) | Resolved, Closed | Reopened | requester (a Closed ticket only within 7 days of closing); staff without the limit |
| assign | any open status | Assigned (OnHold stays; no assignee → back to Submitted) | `tickets.assign`; the person must hold `tickets.transition` |

## Rules enforced (each has a test)
- **Visibility:** requesters see tickets they requested or filed; staff see tickets of their teams, assigned to them, or all with `tickets.read_all`; anything else answers **404**. A ticket without a team is invisible to agents until a supervisor routes it.
- **Numbers** `GA-YYYY-NNNNNN` come from one atomic upsert in the same transaction as the insert: per organisation and year, no gaps from failed saves, no duplicates (unique index as a backstop).
- **A ticket remembers what it was made with:** service name, form version and normalised answers; the detail page labels answers from that version, so later catalog edits never rewrite it. Priority and team default from the service.
- **Creation** goes through the same answer check as the portal (`CheckAnswersAsync`): required, hidden-question and reference rules apply; errors come back per question as `answers.<key>`. Requests on someone's behalf need `tickets.update`.
- **Concurrency:** every state change carries the ticket `version`; a stale copy is 409. Comments don't change the version.
- **Internal notes** are hidden from requesters (server-side filter, not UI) and need `tickets.comment_internal` to write; finished tickets take no comments.
- **Auditing:** every change writes an audit row with diff (`ticket.create|assign|start|hold|resume|resolve|cancel|confirm|reopen|update|comment|note|auto_close`) *and* a timeline event with the actor's name at the time.
- **Tenant isolation:** another organisation's ticket is 404 for every route; numbers count separately per organisation.
- **Privacy of the helper lookups:** `/api/portal/people` needs ≥ 2 characters, returns ≤ 20 matches, and exposes name and department only (no e-mail or phone).

## Verification
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16, Testcontainers) | **439 passed, 0 failed** — includes 12 pure workflow tests (full matrix) and 17 ticket integration tests |
| `tsc --noEmit`, `eslint` on new files, `next build` | pass |
| Playwright `tickets.spec.ts`: requester finds a service, sees server-side validation, sends; supervisor assigns, starts, writes an internal note and a public comment, resolves; requester sees only the public comment, confirms; a stranger gets "not found" and no queue; axe on the form and on both ticket views | **2 passed** |
| Full Playwright suite (existing + new specs), incl. axe light + dark for `/requests`, `/requests/new`, `/tickets` | **122 passed, 0 failed** |
| Frontend `vitest` | still cannot run on Node 20.16 (vitest 5 needs ≥ 22.12) |

Findings from the browser run that were fixed: after posting an internal note the toggle stayed on, so the next reply would also have been internal — it now resets to a public reply; the comment form carried a redundant `aria-label` that duplicated its textarea's name.

## Decisions and known gaps
- Requesters need **no permission**: create, "my requests", comment, cancel, confirm and reopen are open to any signed-in person and decided per ticket. Staff capabilities are the six permissions above.
- Timing constants are in code (`TicketWorkflow`): auto-close after **3 days**, reopen window **7 days**. They become per-organisation settings with the SLA/policy slice.
- Not in this slice: attachments and photo/file questions (need the storage port), approvals (`PendingApproval/Approved/Rejected`), assignment rules (round-robin, least-loaded, skills), SLA clocks and due dates, notifications (nothing is e-mailed yet), watchers, merge/split/link, saved views, kanban, bulk actions, CSAT, work orders, @mentions, comment editing, e-mail-to-ticket.
- The queue shows a team's tickets to its members; "my queue" (assigned to me) is a filter away, not yet a saved view.

## Next
Assignment rules and routing → SLA clocks and escalation (uses `WorkingCalendar`, ticket priority and the service's SLA link) → approvals (e-mail link) → notifications → dashboards, then attachments.
