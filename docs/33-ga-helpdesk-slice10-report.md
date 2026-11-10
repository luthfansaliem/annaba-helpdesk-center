# 33 — GA Helpdesk, R1 slice 10: approvals while away, and managers

Status: **implemented and verified** (API against PostgreSQL, browser flows and accessibility in Chromium). Part of release R1 (see `23-ga-helpdesk-prd.md`). Requirements covered: **APR-004** (delegation for out-of-office, reassignment) and **APR-002** (manager resolution, fallback). The immutable trail (APR-006) and "nobody approves their own request" (APR-004) are kept and extended.

## Why this slice
Approvals worked only while every approver was at their desk: a request waiting for someone on leave simply sat there, and the only remedy was an administrator editing the service's policy for everyone. "The requester's manager" — the most common GA rule — could not be expressed at all because the system did not know who reports to whom.

## What was built
| Area | Where |
|---|---|
| **Reporting line**: a manager per person (`user_profiles.manager_id`), set by people who may update users, validated (active person, not themselves, no loop) | `UserAdminService.SetManagerAsync`, `PUT /api/users/{id}/manager`, `UserDetail.managerId/managerName`; UI: Users → person → Profile → *Reports to* |
| **"The requester's manager" as an approval step** (alone or together with named people/roles); the policy's fallback person decides when there is no manager | `ApprovalStepDef.RequesterManager`, `ApprovalEngine.Validate`, `TicketApprovalService.ResolveAsync`; UI: a checkbox on each step in Setup → Approval Policies |
| **Out-of-office delegation**: a period and a cover; approvals that would go to the person go to the cover | `ApprovalDelegationService.cs`, entity `ApprovalDelegation`, `approval_delegations` table; UI: *Out of office* panel on Approvals |
| **Hand over** one waiting approval to someone else (the approver themselves, or `approvals.manage`) | `TicketApprovalService.ReassignAsync`, `POST /api/approvals/{id}/reassign`; UI: *Hand over* button on each waiting approval |
| **Trail**: `ticket_approvals.on_behalf_of_id`; the ticket, the approver's inbox, the notification and the audit record all say whom a decision was for | `TicketApprovalItem.OnBehalfOfName`, `MyApprovalItem.OnBehalfOfName`; "(for Maya Putri)" |
| One migration, `ApprovalDelegation` (a table and two columns; nothing existing is altered) | `Persistence/Migrations` |

## Rules
- **Applied when an approval is made, and again when it becomes the person's turn.** Someone who goes away after a multi-step request was made is replaced when their step activates (rows waiting for a later step are re-checked then).
- **Hand-over at the start.** A delegation that is already running when it is created moves what is *already waiting* to the cover, and the answer says how many moved and how many had to stay.
- **One level only.** A cover's own delegation is not followed, so there are no chains or loops.
- **The cover is never the requester or creator** of the request, and never a second approver of the same step: if the cover made the request, the approval stays with the person it was for; if the cover is already named in the step, the absent person is dropped (an *all* step is not asked twice).
- **A delegation covers a period, not a role.** Between `starts_at` and `ends_at` (at most 180 days, no overlap with the person's other live delegations; the UI works in whole days on the person's own clock); ending one early does not move anything back — decisions already handed over stay with the cover.
- **The approval trail stays honest.** The row keeps `approver_id` = who decided and `on_behalf_of_id` = who it was for (kept as the *first* person if it is handed over more than once). Decided rows are still never changed.
- **A hand-over** voids the old e-mail link and sends the new approver a fresh one-time link and a notification; it is refused for a decided approval (`409`), for the requester or creator, and for someone already approving that step.
- **Manager step without a manager**: the policy's fallback person decides; with no fallback the request cannot be made and says "No approver is available for the step…" (unchanged behaviour for any empty step).

## Verification
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16, Testcontainers) | **569 passed, 0 failed** |
| `DelegationTests` (7): who may set a manager, own-manager / inactive / loop refusals, clearing, audit; a manager step with a manager, with only the fallback, and with neither; a delegation re-routes new approvals, shows both names on the ticket, in the inbox, in the notification and in the audit record, the away person cannot decide the cover's row, ending it sends the next request back; the cover who made the request, a cover already in the step, and a delegation that has not started; a delegation that starts now takes over what is waiting (and keeps the one that cannot move) and the old e-mail link answers `410`; every validation message, overlapping and back-to-back periods, anonymous and other people's delegations; hand-over by the approver and by an administrator, every refusal, the trail, and no hand-over after a decision | pass |
| `tsc --noEmit`, `eslint`, `next build` | pass (one existing `<img>` warning) |
| Playwright `delegation.spec.ts` (3): Maya goes out of office, her cover Daniel sees the request "for Maya Putri" and approves, the ticket is released and its trail names both, ending the delegation sends the next request back to Maya; a waiting approval handed over in the dialog; a manager set on a profile, a manager step reaches that manager, the policy screen shows the step, the manager is cleared; axe on the out-of-office form | **3 passed** |
| Full Playwright suite incl. axe light + dark and the responsive checks | **158 passed, 0 failed** |

## Decisions and known gaps
- **No chains, no "delegate of a delegate".** Simpler to reason about and impossible to loop; a person who is both away and covering someone must be handled by a hand-over.
- **Escalation of long-waiting approvals** (a reminder, then the manager's manager) is not built; neither are reminders for approvals nobody has touched. The notification of a new approval is the only nudge.
- **No import or sync of the reporting line** (HRIS, CSV): managers are set one by one in the profile. The CSV user import does not carry a manager column yet.
- A step asks for the manager of the **requester** (the person it is for), not of whoever filed it on their behalf.
- An administrator can hand over any approval but cannot create a delegation *for* somebody else; leave cover is set by the person themselves.

## Next
Per-user **notification preferences** and per-tenant templates (NTF-002), then delivery tracking with a dead-letter view (NTF-004); after that the pilot checklist (privacy/retention policy, load test, in-app help), or release R2.
