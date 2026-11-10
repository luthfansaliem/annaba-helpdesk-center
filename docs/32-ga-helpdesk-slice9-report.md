# 32 — GA Helpdesk, R1 slice 9: queue productivity (filters, bulk actions, saved views)

Status: **implemented and verified** (API against PostgreSQL, browser flows and accessibility in Chromium). Part of release R1 (see `23-ga-helpdesk-prd.md`). Requirements covered: **TKT-011** (saved views, filters, bulk actions) and the search part of **TKT-015**. Not covered from those two: the kanban board layout, bulk *tags* (tickets have no tags yet) and full-text search ranking (see gaps).

## Why this slice
After slices 1–8 a helpdesk can be set up and can take requests, but an agent or supervisor working a queue of a few hundred tickets still had to open each ticket to assign, re-prioritise or start it, and rebuilt the same filters every morning. This slice makes the daily queue work fast without adding any new way around the rules.

## What was built
| Area | Where |
|---|---|
| **More filters** on `GET /api/tickets`: `assignedToMe`, `serviceId`, `locationId` (a place *and everything inside it*), `from` / `to` (created range); the text search also reads the **description** | `TicketService.ListAsync`, `TicketQuery` |
| **Bulk actions** `POST /api/tickets/bulk`: assign, set priority, start, hold, resume, resolve, cancel — up to 100 tickets, with a per-ticket result | `TicketService.BulkAsync`, `TicketEndpoints.cs` |
| **Saved views**: personal filters, optionally shared; CRUD on `/api/ticket-views` | `TicketViewService.cs`, `TicketViewEndpoints.cs`, entity `TicketView`, migration `TicketViews` (one new table) |
| UI: the queue gets place and date filters, "Assigned to me", row selection with a **bulk toolbar** and a result panel, and a **Saved views** bar (apply, save, update, remove, share) | `src/app/(app)/tickets/page.tsx`, `src/components/tickets/bulk-bar.tsx`, `src/components/tickets/views-bar.tsx` |

## How bulk actions behave
- **No new rules.** Each ticket goes through the same method as a single change (`AssignAsync`, `UpdateAsync`, `TransitionAsync`), so state-machine checks, who-may-do-what, SLA recalculation, notifications, timeline entries and per-ticket audit events are exactly the one-ticket ones. One extra audit entry, `ticket.bulk`, records the action and the counts.
- **Check first, then act.** The whole request is validated before anything changes (ticket count 1–100, a known action, the fields that action needs, the action's permission). A bad request changes nothing.
- **Partial success is normal and visible.** A ticket that cannot take the change — already closed or cancelled, wrong status for the action, not allowed for this person — is skipped and returned as `ok: false` with the reason; the others still go through. The screen says "2 of 3 tickets updated" and lists the failures by ticket number.
- **No information leak.** A ticket the person may not see is reported as "Ticket not found.", the same as an id that does not exist, and is never touched.
- **Permissions.** Assigning needs `tickets.assign`, changing priority needs `tickets.update`; each button is shown only to people who hold it, and the server enforces it (`403`). Anyone without queue access is refused.
- **Safe after a failure.** The unit of work is reset after a refused ticket, so a half-applied change can never leak into the next ticket's save.
- Not offered in bulk on purpose: *confirm* and *reopen* (requester decisions), *resolve* in the UI (it needs a note per case; the API accepts one shared note).

## How saved views behave
- A view is only a set of **filter values** under the queue's own names, validated on the way in (known keys, known statuses/priorities/SLA states, ids, `yyyy-MM-dd` dates, search text ≤ 100). Blank and `false` values are dropped, so what is stored is minimal and can always be applied again.
- **Personal by default**; nobody else sees or can change it (`404`). People who may assign tickets can **share** a view with everyone who reads the queue; a shared view appears marked *shared*, is read-only for people who cannot assign, and can be maintained by its owner or any assigner. Only the owner decides whether it is shared.
- Names are unique per person (ignoring case) and among shared views; at most 30 personal and 50 shared views.
- *Assigned to me* in a shared view means the **viewer**, not the author.
- Dates are days on the person's own clock: the UI turns `from` / `to` into the first moment of the day and the first moment of the next day.

## Verification
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16, Testcontainers) | **562 passed, 0 failed** |
| `QueueTests` (5): place filter (a floor, a building and a site each find what is inside), service, created range, *assigned to me*, *unassigned*, description search, filters combined; bulk assign / priority / start / hold with a mix of live, cancelled and unknown tickets, the single-ticket timeline entries and the `ticket.bulk` audit entry; validation of every field before anything changes, `403` for an agent assigning, a requester, anonymous, and an agent of another team reaching none of the tickets; views: clean filters, private vs shared, who can read/edit/delete, `403` for requesters and for sharing without `tickets.assign`; name, filter and count limits | pass |
| `tsc --noEmit`, `eslint`, `next build` | pass (one existing `<img>` warning) |
| Playwright `queue.spec.ts` (3): select all, set priority (the cancelled ticket is reported by number and reason), assign, start work; a filter set is saved with the server's field message, restored after a reload and removed with confirmation; a requester gets `403` from both new APIs; axe with a selection and with views | **3 passed** |
| Full Playwright suite incl. axe light + dark and the responsive checks for `/tickets` | **155 passed, 0 failed** |

Findings on the way: the slice 8 quick-start end-to-end test started failing in full runs with "Too many attempts" — invitation acceptance is limited to 10 per 15 minutes per client address, and the suite (now faster) used them all from one address. That is the limiter working as designed, so the test now presents its own address through `X-Forwarded-For` (which the API trusts only from the authenticated proxy).  axe flagged the low contrast of the *soft* button style on the "Clear selection" button (replaced by the standard outline button); my first end-to-end expectation assumed a cancelled ticket would accept a new priority — the server rightly refused it, so the test now asserts the correct "2 of 3" outcome.

## Decisions and known gaps
- Bulk operations run one ticket at a time inside the request (≤ 100). Each is its own transaction, which is what makes partial success possible; a long-running job with progress is not needed at this size.
- Search is `ILIKE`-style substring matching on number, subject, description and requester name/e-mail, limited to the tickets the person may see. There is no ranking, stemming or search over answers and comments yet (TKT-015's full-text index is a later step, with the volume that makes it worthwhile).
- **Not built:** the kanban board, bulk tags (no tags yet), saved views for the "my requests" list, and a default view per person.
- The per-address limiters read the first `X-Forwarded-For` entry the proxy passes on, so the edge proxy in front of the UI must overwrite (not append to) that header; Traefik/Coolify does by default, but it is worth confirming for any other proxy.
- Views store filter names, not ids of a layout, so a future filter simply adds a key; a view saved today keeps working.

## Next
The remaining R1 P0 items in `23-ga-helpdesk-prd.md`, in the order of value: approval **delegation** and manager resolution (APR-002/004), per-user **notification preferences** and per-tenant templates (NTF-002), delivery tracking with a dead-letter view (NTF-004), then the pilot checklist (privacy/retention policy, load test, in-app help).
