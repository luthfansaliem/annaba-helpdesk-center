# 45 — GA Helpdesk, R2 slice 5: preventive maintenance

Status: **implemented and verified** (API and Playwright suites on the final code; see the table at the end). Fifth slice of release R2 (`40-ga-helpdesk-r2-plan.md`): **PM-001** (plans by asset, asset type or place, with a checklist and a default person, team and vendor), **PM-002** (work made ahead of the date, caught up after downtime, never duplicated) and **PM-003** (compliance report per plan, team, vendor and site). Meter-based triggers (PM-004, P2) are not part of this slice.

## What was done
1. **Maintenance plans** (*Helpdesk → Maintenance*): a name, the service the visits are filed under (it may be unpublished, so only staff see it), what is serviced — **one asset**, **every asset of a type** (optionally only in a place and the places below it) or **a place** — how often (every *n* days, weeks, months or years, from a first date, optionally until a last date), how many days ahead the work is made (0–90, default 7), a saved checklist, instructions, and who does it (team, person, vendor; the after-photo rule).
2. **Visits.** For every date within the lead time the scheduler makes one visit per target: a **ticket** on the plan's service (with the asset and place, so it shows in the asset's history) carrying one required **work order** with the plan's checklist and people, due at the end of the date in the organisation's time zone. The ticket resolves itself when the work order is done. Visits are made right after a plan is saved or resumed, and then by the daily reminders worker.
3. **Catch-up without a pile of stale work.** If the scheduler was down (or a plan was behind), every date that the *next* date has already overtaken is recorded as **missed** — no ticket, but visible in the report and audited (`pm.catch_up`) — and only the current date gets its work. Pausing is deliberate: when a plan resumes, the dates that passed are skipped, not missed.
4. **Never twice.** Each date is claimed by moving the plan's counter with a conditional update inside the transaction that creates its visits, and visits are unique per plan, target and date. Two scheduler instances, a retry or a crash half-way cannot create a visit twice (tested with two runs at once).
5. **The plan page** shows the next five dates, how many assets it covers now, the latest 50 visits (planned, overdue, done on time, done late, cancelled, missed) with links to the asset and the work order, and the last 12 months' counts and compliance. Plans can be edited, paused, resumed, and removed while they have no visits.
6. **Compliance report** (*Maintenance → Compliance*): for a period of up to a year, planned visits, done on time, done late, overdue, missed or cancelled, not due yet, and the percentage, grouped by plan, team, vendor or site (the top-level place).
7. **Work orders (small change).** Completing the last required work order now also resolves a ticket that was never started (*submitted*, *assigned* or *reopened*): it is started on the way. Planned visits need this — the technician usually goes straight to the work order — and it holds for any ticket that resolves with its work orders.

## Rules worth knowing
| Rule | Why |
|---|---|
| `pm.read` for GA Agent and up; `pm.manage` for GA Supervisor and GA Manager. | Planning the routine work is a supervisor's job; everyone doing the work can see the plan. |
| Visits' tickets have **no SLA clock**; the work order's due date is the target. | A resolution clock started 7 days ahead would always breach and pollute the SLA report. |
| Visits are filed for the plan's **owner** (who last saved it), on the plan's service, without form or approval. | Planned work is already decided; someone must be the requester of record. |
| A new or changed rhythm starts from today; earlier dates of a schedule that did not exist are not "missed". | The report must not punish a plan for dates before it existed. |
| On time = the work order completed by the end of the due date (organisation's time zone). | One clear definition, the same as the vendor scorecard. |
| Compliance % = on time ÷ (planned − not yet due); missed, cancelled and overdue count against it. | A visit that was planned and not done is not compliance. |
| A blocked vendor or an inactive person on the plan is left off new work orders (the ticket says so for the vendor). | Never give work to someone who can't take it. |
| At most 100 assets per plan, 500 missed rows per catch-up run; a failure is audited (`pm.generate`, failure) and retried at the next run. | Bounded work per run; problems are visible in the audit log. |

## Tests added
**API — `MaintenanceTests` (4):** validation (name, service, target, unit, every per unit, end before start, lead days, duplicate name) and permissions (agents read but can't create, requesters refused); a far-future plan makes nothing, shows its next five dates and can be removed; a plan for a place makes its visit at once with the place on the ticket, the service's team and no SLA, and can't be removed afterwards. A type-in-a-place plan covers only the active assets in the place and below it (not elsewhere, not retired); the work order carries the name, instructions, person, vendor and checklist and notifies the person; a second scheduler run adds nothing; completing the work order resolves the assigned ticket and the visit is *done on time*; list filters by name and by asset. In an isolated organisation: three weeks of downtime record three missed dates and make only the current one, a second run makes nothing, the catch-up is audited, the compliance report counts 4 planned / 3 missed / 1 not due / 0 %, groups by vendor ("In-house"), and refuses a reversed or over-a-year range and an unknown grouping; a paused fortnight makes and misses nothing and resuming makes today's visit only; two scheduler runs at the same moment make the next visit once. Plus the pure date rules (month ends do not drift, first date on or after, visit states, upcoming dates up to the end).
**Playwright — `maintenance.spec.ts` (1) and `/maintenance` and `/maintenance/compliance` in the accessibility sweep (light and dark):** a plan for every asset of a type in a place is created in the drawer; its page shows the first visit already made (asset tag, *Planned*, due date) and five next dates; the work order carries the checklist; pause and resume; no *Remove* once it has history; the compliance report lists it (no percentage yet) and groups by site; a requester gets `403`; the list, the plan page and the report pass the accessibility check.

## Decisions and known gaps
- **Rhythms** are every *n* days, weeks, months or years from a first date; full calendar rules (RRULE: "the second Tuesday", several days a week) are not offered.
- **Meter-based triggers** (PM-004, kilometres or running hours) are P2 and not done.
- **One work order per visit**; several steps belong in the checklist. Plans for a type make one ticket per asset (at most 100 per date).
- **No plan list on the asset page** yet (the API has `?assetId=`); the asset's ticket history already shows each visit.
- Visits whose ticket was deleted by retention show as *removed* and are left out of the percentage.
- **Existing organisations** must add the permissions to their GA roles by hand (runbook).

## Verification of the functional behaviour
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16, Testcontainers) | **624 passed, 0 failed** (620 before; +4 `MaintenanceTests`) |
| `tsc --noEmit`, `eslint`, `next build` | pass (one pre-existing `<img>` warning in the app icon) |
| Full Playwright suite (light + dark accessibility, responsive) | **203 passed, 0 failed** (198 before; +1 maintenance, +4 accessibility pages) |

## Next
R2-6: satisfaction (CSAT) — a rating and comment after resolution, a low-score alert, and a report per agent, team and site.
