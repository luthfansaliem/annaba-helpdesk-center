# 43 — GA Helpdesk, R2 slice 3: work orders and checklists

Status: **implemented and verified** (API and Playwright suites on the final code; see the table at the end). Third slice of release R2 (`40-ga-helpdesk-r2-plan.md`): **TKT-006** (work orders with their own assignee, checklist, labour time, parts and cost; the ticket resolves when the required ones are done, configurable), **TKT-007** (checklists per work order with required lines blocking completion; photo before/after evidence) and the manual part of **TKT-014** (time per person per work order).

## What was done
1. **Work orders on a ticket.** A supervisor or manager adds them from the ticket's new *Work orders* panel: what needs doing, instructions, a team and/or a person (or nobody: anyone allowed may take it by starting it), a due date, whether it is **required**, whether it **needs a photo of the finished work**, and a checklist. They are numbered after the ticket (`GA-2026-000123-W1`, `-W2`…) and take the ticket's asset.
2. **Checklists and saved checklists.** Lines can be typed per work order or copied from a saved checklist (*Helpdesk Setup → Checklists*, for example "AC service"); required lines must be ticked before completing. Changing a saved checklist never changes work orders that already copied it. Preventive maintenance (R2-5) will reuse them.
3. **Doing the work** (*Helpdesk → Work Orders*, "Mine" or everything visible, or from the ticket): start, put on hold (with what you are waiting for), resume, tick the checklist, add **before / after photos**, log **time** (minutes per day, 90 days back at most) and complete with a note. The page says exactly what still blocks completing. A manager can cancel (with a reason).
4. **Parts and costs** (part, labour, service, other; quantity × unit cost; totals per work order) for people with `workorders.costs` — agents log time and photos but never see money.
5. **The ticket.** A ticket cannot be resolved while a required work order is open; cancelling a ticket cancels its open work orders; and with *Resolve the ticket when its required work orders are done* (on the panel or when creating one), finishing the last required work order resolves the ticket by itself (code *fixed*, a note listing the work orders, audited as `ticket.auto_resolve`). Optional work orders never hold or resolve the ticket. Every step is a **staff-only** line on the ticket's timeline.
6. **Notifications**: `workorder.assigned` (to the person given the work, also by e-mail) and `workorder.done` (to the person working the ticket, in the app); both switchable per person.

## Rules worth knowing
| Rule | Why |
|---|---|
| Visible to people who can see the ticket, **and to the person it is given to** even outside the ticket's team (a contractor-like colleague sees that work order only, not the ticket or its other work orders). | Give work across teams without widening ticket access. |
| Only the person it is given to (or anyone, when nobody is, by starting it) does it; managers can too. Cancelling is for managers. | Clear ownership; no one ticks someone else's checklist. |
| A work order on a **closed, cancelled or rejected** ticket can't change; on a **resolved** ticket optional work can still be done. | Finished requests are frozen; resolving doesn't strand optional follow-ups. |
| The checklist can only be ticked while the work order is in progress; a ticked line stays ticked when a manager edits the list. | Evidence of who did what, when. |
| Photos are stored with the ticket's files (same type checks, virus scan, 8 MB, 20 per ticket) and served with the same sandboxing headers; the work order has its own download link so its assignee can open them without seeing the ticket. | One file pipeline, one set of protections. |
| Loading a work order for a change reads it again by id. | Found while testing: the visibility query includes the ticket scope, and that makes the whole EF query untracked, so changes were not saved; now covered by the tests. |

## Tests added
**API — `WorkOrderTests` (7):** who creates, sees and does (requester and stranger excluded; an assignee outside the team sees only their work order; "my work orders"; assignment notification); a checklist from a saved one plus own lines, ticking only after starting, completion blocked by required lines and the missing after photo and a missing note, the stage check, the after photo and its download, completion, staff-only timeline lines, an archived checklist refused; only the assignee works an assigned order, unassigned work is taken by starting, hold needs a reason, impossible moves, cancel only by managers; time validation and removal rules, costs only with the permission (agents see `null`), totals and removal, nothing on a cancelled order; required orders block resolving, the last one done resolves the ticket with the listed numbers while an optional one stays open; without the setting the ticket waits, and cancelling the ticket cancels its open orders and refuses new ones; editing keeps ticked lines, reassigning notifies, stale versions are refused.
**Playwright — `workorders.spec.ts` (2) and two pages in the accessibility sweep (light and dark):** a saved checklist is created; a work order is made from it on a ticket in progress, needing an after photo and resolving the ticket; it is started (and taken), the missing items are shown, the lines are ticked, an after photo uploaded, time logged and a part costed (total shown), then completed — and the ticket shows *Resolved* with the automatic note; it appears under *Done* in the list; a requester sees no work orders.

## Decisions and known gaps
- **Vendors** cannot be given a work order yet (R2-4); a **timer** for time tracking is not offered (manual entries only); labour cost is entered as a cost line (no hourly rates).
- **No currency field**: amounts are in the organisation's currency; multi-currency is R4.
- **Cost reports** (per asset, site, vendor) come with R2-8; this slice stores the lines.
- **Requesters do not see work orders**; they see the ticket's progress as before. Photos taken as evidence are ticket files, so they appear in the ticket's attachment list for everyone who can see the ticket — take that into account for sensitive places.
- **Existing organisations** must add the permissions to their GA roles by hand (runbook).

## Verification of the functional behaviour
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16, Testcontainers) | **616 passed, 0 failed** (609 before; +7 `WorkOrderTests`) |
| `tsc --noEmit`, `eslint`, `next build` | pass (one pre-existing `<img>` warning in the app icon) |
| Full Playwright suite (light + dark accessibility, responsive) | **195 passed, 0 failed** (189 before; +2 work orders, +4 accessibility pages) |

## Next
R2-4: vendors and contracts — a vendor register, giving a work order to a vendor, contract expiry reminders.
