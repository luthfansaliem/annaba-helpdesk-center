# 29 — GA Helpdesk, R1 slice 6: operations dashboard and SLA report

Status: **implemented and verified** (API against PostgreSQL, browser flows and accessibility in Chromium). Part of release R1 (see `23-ga-helpdesk-prd.md`). Requirements covered: **RPT-001** (role-aware operations dashboard: open / overdue / at risk, backlog by team, created vs resolved, average first response and resolution, aging) and **RPT-002** (SLA report: attainment by priority, team and service; breach list with the missed target). Not covered: backlog by *site*, attainment by site/vendor (no vendors yet), the SLA breach *reasons* beyond which target was missed, CSAT and agent performance (RPT-006), cost/asset/utilisation reports, and exports/scheduled mail (RPT-007).

## What was built
| Area | Where |
|---|---|
| `ReportService`: the two read-only reports | `api/src/AnnabaDigital.Application/Helpdesk/ReportService.cs` |
| `TicketService.QueueScopeAsync`: the one definition of "tickets this person may see in staff views", now shared by the queue and the reports | `TicketService.cs` |
| Routes `GET /api/reports/operations?days=` and `GET /api/reports/sla?days=` | `api/src/AnnabaDigital.Api/Endpoints/ReportEndpoints.cs` |
| UI: Helpdesk → **Operations** (`/reports`) and **SLA Report** (`/reports/sla`), 7 / 30 / 90-day period | `src/app/(app)/reports/`, `src/components/reports/parts.tsx` |

No schema change: the reports read what slices 3–5 already store.

## What the numbers mean
- **Who sees what (role-aware).** The reports use the same visibility as the ticket queue: a team member sees their teams' tickets and those assigned to them; holders of `tickets.read_all` (supervisors, managers, administrators) see everything; anyone without a ticket read permission gets 403. There is no separate "reports" permission, so a report can never show more than the queue would.
- **Period.** The last *N* days (clamped to 1–90), counted in whole UTC days from the start of the day *N − 1* days ago.
- **Open** = waiting for approval, submitted, assigned, in progress, on hold or reopened. A resolved ticket awaiting confirmation is not backlog. **Overdue** = open and SLA state *breached*; **At risk** = open and *at risk*; **Unassigned** = open, no assignee, not waiting for approval.
- **Created / resolved per day** use the ticket's creation day and its (latest) resolution day. **Average first response** and **average resolution** are *clock* time (not working time), over tickets created / resolved in the period.
- **SLA attainment** = tickets *resolved in the period* that had a policy: met ÷ (met + missed). A reopened ticket leaves the cohort (its resolution is cleared) and enters it again, with a fresh clock, when it is resolved again. With no data the report shows "—", never 0 %.
- **Needs attention** = open tickets that are breached or at risk, breached first, then by deadline (10 rows).
- **Breaches** = tickets whose first breach happened in the period (the breach instant is permanent), newest first, at most 50, with the target that was missed first (*first response* when the breach coincides with the first-response deadline and nobody answered in time, otherwise *resolution*).
- **Aging** buckets use the ticket's age (creation to now) for open tickets, not the period.

## Verification
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16, Testcontainers) | **502 passed, 0 failed** |
| 4 new integration tests: permissions; **exact** numbers for a team member (open, overdue, unassigned, created, resolved, attainment, backlog by team, status, priority, aging, attention list, daily series) while another team's ticket stays invisible and a supervisor sees it; the SLA report (met/missed per priority, team and service, open overdue, breach list with reasons, a 7-day window); clamping and empty scopes | **pass** |
| `tsc --noEmit`, `eslint` on new files, `next build` | pass |
| Playwright `reports.spec.ts` (KPIs, charts, period switch, SLA tables, a requester is turned away, axe on both pages) | **3 passed** |
| Full Playwright suite (existing + new specs, one shared database) incl. axe light + dark for `/reports`, `/reports/sla` | **144 passed, 0 failed** |
| Frontend `vitest` | still cannot run on Node 20.16 (vitest 5 needs ≥ 22.12) |

Findings fixed on the way: three narrow tables side by side needed horizontal scrolling, which axe flagged (a scrollable area that keyboard users cannot focus); they are now stacked at full width. One test expectation was wrong, not the report: a ticket created 30 days ago is correctly outside a 30-day window (the window starts at the beginning of the day 29 days ago).

## Decisions and known gaps
- Reports run live aggregate queries over at most 20,000 rows of the period; they do not read replicas or rollups (RPT-008 arrives when volume requires it, release R4). They never write.
- "Backlog by site" needs a ticket-to-site rollup through the location tree and is deferred; the team view is the planning view for now.
- The breach reason is inferred from stored deadlines; if a priority change recalculated the first-response deadline after the breach, a first-response breach can be shown as *resolution*.
- Exports (CSV/XLSX/PDF) and scheduled reports (RPT-007) are not built.

## Next
Attachments and the storage port (TKT-005), then the R1 hardening pass (onboarding wizard, GA role seeds review, staging runbook for the helpdesk, end-to-end demo data), before release R2 (work orders, assets, preventive maintenance).
