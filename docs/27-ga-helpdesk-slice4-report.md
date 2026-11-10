# 27 — GA Helpdesk, R1 slice 4: assignment rules and SLA clocks

Status: **implemented and verified** (API against PostgreSQL, browser flows and accessibility in Chromium). Part of release R1 (see `23-ga-helpdesk-prd.md`). Requirements covered: **TKT-003** (assignment rules, round robin, least loaded; skills and shifts later), **SLA-002** (policies, first match, per priority/service/category/place), **SLA-003** (the working-time engine from slice 1 now drives real clocks), **SLA-004** (pause while on hold, recalculation on priority change), **SLA-005** (escalation thresholds, recorded; notifications and reassignment come later), **SLA-006** (state on every ticket, filterable and sortable). **SLA-001** (impact × urgency priority matrix) is *not* built: priority is still chosen from the service and edited by staff.

## What was built
| Area | Where |
|---|---|
| **SLA clock** (pure): due times in working time, share of target used, escalation level, state, pause / resume / restart | `api/src/AnnabaDigital.Domain/SlaClock.cs` |
| **Routing logic** (pure): condition matching (service, category with its parent, place with everything inside it, priority), first match, round robin, least loaded | `api/src/AnnabaDigital.Domain/Routing.cs` |
| Entities `SlaPolicy`, `AssignmentRule`; ticket gains SLA facts and `assigned_at` | `Entities/Sla.cs`, `Entities/Tickets.cs` |
| `SlaService` (policies, applying and refreshing a ticket's clock, periodic check, default policies), `AssignmentService` (rules, routing, preview) | `api/src/AnnabaDigital.Application/Helpdesk/` |
| Endpoints `/api/sla/*`, `/api/assignment-rules/*` (one permission per route); ticket list gains `sla` filter and `sort=due` | `Endpoints/SlaAndRuleEndpoints.cs`, `TicketService.cs` |
| Tables `sla_policies`, `assignment_rules`, new ticket columns; migration `SlaAndRouting` | `AnnabaDigitalDbContext.cs`, `Persistence/Migrations/*_SlaAndRouting.cs` |
| Permissions `sla.read/manage`, `rules.read/manage` (Supervisor: read; **GA Manager**: manage) | `Permissions.cs` |
| Background `SlaWorker` (every 5 minutes; switch `Sla:Enabled`, `Sla:IntervalMinutes`) | `EmailInfrastructure.cs` |
| UI: Helpdesk Setup → **SLA Policies** and **Assignment Rules** (ordering, edit, "test a ticket" panels); ticket page **Service level** panel; queue gets an SLA column, filter and "sort by deadline"; requesters see "Expected by" only | `src/app/(app)/setup/sla`, `setup/assignment-rules`, `tickets/` |

## How it works
**Policies.** Each organisation starts with four policies (urgent 15 min / 4 h, high 1 h / 8 h, standard 4 h / 24 h, low 8 h / 48 h — *working* minutes on the default calendar, escalation at 50 % and 80 %). A policy can be narrowed to a service, a category, a place (and everything inside it) and a priority, and can name its own calendar; otherwise the calendar of the ticket's place (or the nearest parent with one) is used, then the organisation default. Policies are tried in the saved order; the first match wins.

**The ticket keeps a copy.** At creation (and when its priority or place changes) the ticket takes the matching policy's numbers, so editing a policy never moves the goalposts of open tickets. Everything derives from four stored facts — clock start, working minutes spent paused, first response time, resolution time — so a recalculation is just another call and gives the same answer for the same inputs.

**Clock rules.** *First response* = the first public comment by staff (not the requester, not an internal note) or starting work, or resolving. *Hold* stops the clock; *resume* adds the **working** time spent on hold to both deadlines (a night on hold costs nothing). *Priority/place change* re-matches the policy but keeps the start and the time already paused. *Reopen* starts a fresh resolution cycle. *Resolved in time* → **Met**; late, or a target missed while open → **Breached**, and the instant of the first breach is kept forever even after a reopen.

**Escalation.** Levels 1 (first threshold), 2 (second threshold = **At risk**) and 3 (breach) are each recorded **once**, as an internal "System" entry on the ticket's timeline and an audit row (`ticket.sla_escalation`). Requesters never see them. The level only rises within a cycle. The periodic check also keeps the stored state current for tickets nobody touches; held tickets are skipped (their clock is stopped).

**Assignment rules.** At creation, rules are tried in the saved order; the first match sends the ticket to its team and, with a strategy, to one person. Only active people of that team who hold `tickets.transition` are candidates. *Round robin* = whoever was given a ticket longest ago (never-assigned first; equal times fall to the lighter load); *least loaded* = fewest open tickets, then longest ago; remaining ties fall to the lowest id. No match → the service's owning team and nobody assigned (the slice-3 behaviour). The timeline shows who got it and by which rule. A team that active rules send tickets to can't be archived.

## Rules enforced (each has a test)
Names unique among live items; references must exist (and places, calendars, teams be live); resolution ≥ first response; thresholds 1–98 and rising, at most 99 (also a database check constraint); reordering must list every active item exactly once; stale versions are 409; every change is audited with a diff; permissions are per route (a plain user gets 403, a supervisor can read but not write, a manager can manage); the "test a ticket" panels run the exact matching a new ticket goes through and change nothing.

## Verification
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16, Testcontainers) | **478 passed, 0 failed** — includes 25 pure tests for the clock and routing (pause/resume across non-working hours, levels, breach permanence, restart, tie-breaks) and 14 integration tests |
| `tsc --noEmit`, `eslint` on new files, `next build` | pass |
| Playwright `sla.spec.ts`: policies listed and created, reordered, test panel shows the winner; a rule routes a requester's ticket to a team and person; staff see the clock, filter and sort; requesters see only "Expected by"; axe on both pages and on the ticket with its service level | **2 passed** |
| Full Playwright suite (existing + new specs, one shared database), incl. axe light + dark for the two new pages | **128 passed, 0 failed** |
| Frontend `vitest` | still cannot run on Node 20.16 (vitest 5 needs ≥ 22.12) |

The e2e specs share one database, so `sla.spec.ts` archives the assignment rule it creates (a live rule for the Courier service changed how `tickets.spec.ts` tickets were routed in one full run).

Findings fixed on the way: a check constraint named the escalation columns as `escalate1_percent` but the naming convention produces `escalate1percent` (the integration tests caught it on first run); the migration's new non-null `sla_state` column would have given existing tickets `""` and broken reading them, so it defaults to `None`; round robin tied when several tickets were assigned in the same instant, so load is now the second tie-break. One existing test (`ApprovalNotificationTests`) is timing-sensitive under full load (an e-mail wait) and failed once in 4 full runs; it passed on rerun and its failure message now lists the mails that were sent.

## Decisions and known gaps
- Targets are **working minutes** of a calendar, entered as minutes in the UI (with a plain-language echo).
- Escalation only **records**: notifying a supervisor, raising priority or reassigning on breach need the notifications slice.
- Not built: impact × urgency priority matrix (SLA-001), operational-level agreements for vendors (SLA-007), skills and shifts in assignment, SLA on approval time (arrives with approvals: `PendingApproval` will pause the clock like a hold), per-organisation auto-close/reopen windows.
- Tickets created before this slice have no SLA (state *none*); new tickets get one.
- The periodic check scans at most 2,000 running tickets per run, oldest deadline first; a keyed schedule is a scale item for release R4.

## Next
Approvals (policy per service, e-mail link, pausing the clock) → notifications (in-app and e-mail, including escalations) → dashboards (operations and SLA), then attachments.
