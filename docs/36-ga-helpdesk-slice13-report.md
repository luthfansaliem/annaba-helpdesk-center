# 36 — GA Helpdesk, R1 slice 13: data retention, legal holds and privacy

Status: **implemented and verified** (API suite and Playwright suite re-run on the final code; see the table at the end). Part of release R1 (see `23-ga-helpdesk-prd.md`): this is **ADM-006** (retention policies per data class, legal hold, purge jobs) and the "retention policies active, data export/deletion verified" line of the privacy exit checklist (§14).

## What was done
1. **A retention policy per organisation**, off until an administrator switches it on (*Administration → Data Retention*, permission `retention.manage`). Three data classes: finished tickets, their files, and in-app notifications. Each has its own period or "keep".
2. **Two ways to treat a finished ticket:** *anonymise* (keep the ticket as a statistic, remove what people wrote and its files) or *delete* (remove it completely).
3. **Legal hold** on a single ticket (from its page): a held ticket is skipped by every clean-up and its files cannot be removed by hand.
4. **A daily clean-up** that applies every organisation's own policy (inside the existing `RetentionWorker`), plus *Preview* (what would be removed, changing nothing) and *Run now* on the page.
5. **Privacy for the person:** *Profile → Privacy* says how long their organisation keeps their requests, files and notifications; *Download my data* now also contains their requests, their own comments and the names of the files they attached.

## The rules
| Rule | Why |
|---|---|
| **Off by default.** A saved policy that is switched off removes nothing; an organisation that never saved one has none. Switching on needs at least one period. | A destructive feature must never act on a default. |
| Only **finished** tickets (closed, cancelled, rejected) are aged, from their last update, which for these is the day they finished. Open tickets are never touched. | Nothing in flight is lost; reopening resets the age. |
| Periods: tickets 30–3650 days, files 7–3650 and **never longer than the ticket period**, notifications 7–3650. Also enforced by check constraints in the database. | A file cannot outlive the ticket it belongs to. |
| **Anonymise** removes: subject (replaced by a fixed text), description, answers, resolution/hold/cancel text, every timeline body and every person's name on the timeline ("System" stays), approval comments, addresses and devices, the ticket's notifications, its files (bytes and rows marked removed). **Keeps:** number, service, place, priority, status, all dates and the SLA figures, so reports stay true. The ticket then answers `409` to every change (no reopening). | The statistic survives; what people wrote does not. |
| **Delete** removes the ticket with its timeline, approvals, notifications and file rows (database cascade) and the files' bytes. It no longer counts in reports. | For organisations that must not keep even the statistic. |
| **Legal hold** protects a ticket from all steps; files of a held ticket cannot be removed by hand (`409`). Only people with `retention.manage` see that a hold exists and why. | A dispute or investigation must not lose evidence to a timer. |
| **Files go first, bytes before the row is marked.** A file that cannot be deleted (locked or read-only volume) is not reported as done, is retried next run, raises a security event (`retention.files_failed`), and holds its ticket back until it is gone. | A privacy tool that silently leaves bytes behind is worse than none; a row that points at a missing file is only a "not found". |
| Every change of the policy, every hold and every manual run is **audited with counts only** (never content); policy changes also raise a security event. Saving, running and holding need a **fresh password check**. | Accountability for the most destructive operation in the product. |
| Notifications are removed regardless of a hold on their ticket (they are copies shown in the bell; the ticket's own timeline is what a hold protects). | Keeps the rule simple and the bell clean. |

## How it runs
- **`RetentionService`** (Application): `GetAsync/UpdateAsync/PreviewAsync/RunNowAsync/RunDueAsync`, holds, notice. `RunDueAsync` switches into each organisation whose policy is on and applies it; the pure validation lives in `RetentionRules` (Domain).
- Per organisation and run: files (batches of 200, at most 5,000), then tickets (batches of 200, at most 5,000), then notifications (batches of 2,000). Each batch re-checks its conditions inside the statement/transaction, so a ticket reopened or put under hold in the meantime is left alone and two instances never fight (every step is idempotent).
- The daily `RetentionWorker` calls it after the platform housekeeping (`Retention__Enabled`, `Retention__IntervalHours`, `Retention__StartupDelayMinutes`).
- Migration **`DataRetention`**: `retention_policies`, `ticket_legal_holds`, `tickets.anonymised_at`. New permission `retention.manage` (critical; Super Admin only by default, not in GA Manager).
- API and screens: `08-api-spec.md` §12.8; RBAC in `09-rbac-matrix.md`; operations in the runbook (`22`); data model in `07`.

## Tests added
**API — `RetentionTests` (10, each in its own database, because a run applies to a whole organisation):**
permissions and the notice; validation, fresh password check and audit; nothing removed when off / unfinished / too young; anonymising (content, answers, timeline names, files and bytes, notifications, no reopening, statistics kept, audit has counts only); deleting (cascade and bytes); legal hold (skipped, preview counts it, visible only to managers, files protected, release → removed, audit); files and notifications on their own periods, preview changes nothing; a file that cannot be removed (reported, ticket waits, retried, security event); the scheduled run applies each organisation's own policy and leaves another organisation alone; the personal data export (own requests, comments and files only).
**Playwright — `retention.spec.ts` (2) and two more pages in the accessibility sweep (light and dark):** the administrator's whole path (validation message on the field, preview, save, run, privacy notice read by a member, legal hold placed from the ticket and released from the list, the requester never sees the hold), and a member neither sees the page nor can read the policy. The test only ever saves ten-year periods and switches the policy off again, so it cannot touch other tests' data.

## Decisions and known gaps
- **"Anonymise" is pseudonymisation of the ticket, not of the people.** The ticket still points to the accounts involved (requester, assignee, approvers) by internal id; those accounts are erased when closed (account closure). An organisation that must remove even that link uses *delete*. The page says so next to the choice.
- **Audit and security logs are not governed by this policy**: they are append-only and use the platform's `auditRetentionDays` (shown on the page). Names inside old audit rows stay for that period (unchanged from slice "account closure").
- **E-mails already sent** and **backups** keep what they held until they expire; after restoring a backup the (restored) policy re-applies at the next daily run. Stored files that have no database row (an orphan left by a crash between the two steps of a delete) are found and removed by the file check added in slice 14 (`37-…`).
- **Release time:** a very large backlog is cleared 5,000 tickets a day per organisation; *Run now* repeats it on demand.
- **Not done here (ADM-006 asks for per data class):** visitors, packages and assets do not exist yet; when they do they join the same policy table. Per-service or per-category periods are not offered (one period for all finished tickets).
- **Not verified:** a real clean-up on a staging volume (the tests use a temporary folder and a refusing volume), an object store adapter, and a data-protection review by a lawyer: the wording on the privacy tab is a plain description, not legal advice.

## Verification of the functional behaviour
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16, Testcontainers) | **585 passed, 0 failed** (575 before; +10 `RetentionTests`) |
| `tsc --noEmit`, `eslint`, `next build` | pass |
| Full Playwright suite (light + dark accessibility, responsive) | **167 passed, 0 failed** (161 before; +2 retention, +4 accessibility pages) |

## Next
Pilot checklist items that remain from `23-ga-helpdesk-prd.md` §14: in-app help for the three personas, a restore drill of the files volume together with a database dump, and per-tenant e-mail wording (NTF-002); or release R2 (work orders, assets, preventive maintenance).
