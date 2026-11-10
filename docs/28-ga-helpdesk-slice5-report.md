# 28 — GA Helpdesk, R1 slice 5: approvals and notifications

Status: **implemented and verified** (API against PostgreSQL, browser flows and accessibility in Chromium). Part of release R1 (see `23-ga-helpdesk-prd.md`). Requirements covered: **APR-001** (policy per service: ordered steps, any-one or all-of, people and roles; no cost thresholds yet), **APR-002** (fallback approver; a requester-manager lookup is not built because users have no manager field yet), **APR-003** (approve/reject with a mandatory reason on reject, in the app or through a signed one-time e-mail link), **APR-004** in part (*nobody approves their own request*; delegation and out-of-office are not built), **APR-006** (immutable trail), **SLA-004** for approvals (the clock stops while a request waits), **NTF-001** (in-app and e-mail for the main events), **NTF-002** in part (the existing per-person e-mail preference is honoured; templates per organisation and digests are not built). APR-005 (reminders, timeouts) is not built.

## What was built
| Area | Where |
|---|---|
| Approval rules (pure): what a step's decisions add up to, policy validation, storage format | `api/src/AnnabaDigital.Domain/ApprovalEngine.cs` |
| Entities `ApprovalPolicy`, `TicketApproval`, `Notification`; ticket statuses `PendingApproval`, `Rejected`; timeline kind `Approval` | `Entities/Approvals.cs`, `Entities/Tickets.cs`, `TicketWorkflow.cs` |
| `ApprovalPolicyService` (admin), `TicketApprovalService` (start, decide, e-mail link, trail, withdraw), `NotificationService` (bell + e-mail) | `api/src/AnnabaDigital.Application/Helpdesk/` |
| Six new e-mail templates in English and Bahasa Indonesia (assigned, resolved, replied, approval needed, approval decided, SLA breached) | `Email/EmailTemplates.cs` |
| Routes `/api/approvals/*` and `/api/notifications/*` | `Endpoints/ApprovalEndpoints.cs` |
| Tables `approval_policies`, `ticket_approvals`, `notifications`; migration `ApprovalsAndNotifications` | `AnnabaDigitalDbContext.cs`, `Persistence/Migrations/` |
| Permissions `approvals.read`, `approvals.manage` (Supervisor reads, **GA Manager** manages) | `Permissions.cs` |
| UI: **Approvals** inbox, **Notifications** page, a notification bell and an Approvals badge, **Approval Policies** settings, approval panel and approve/reject on the ticket, public **/approve** page for the e-mail link | `src/app/(app)/approvals`, `notifications`, `setup/approvals`, `src/app/(auth)/approve`, `src/components/tickets/decide-dialog.tsx`, `app-shell.tsx` |

## How it works
**Policy.** Per service: 1–5 ordered steps; each names people and/or roles and says *any one* or *all of them*; an optional fallback approver. One live policy per service (database-enforced). Changing or removing it never touches requests already waiting — they keep the approvers they were made with.

**A new request.** If its service has a policy, the ticket is created as **Pending approval**: no team routing and no assignment yet, its SLA clock stopped, only *cancel* allowed to the requester. Roles are resolved to people at that moment (active, capped at 25 per step). **Nobody can approve their own request**: the requester and, when someone files on another's behalf, the creator are left out of every step. If that leaves a step empty the fallback decides; if there is no usable fallback the request **cannot be made** (409, nothing is saved, the ticket number is not consumed).

**Deciding.** Step 1's approvers are told (in the app, plus an e-mail they always get — approvals are actionable, so the e-mail preference does not mute them) with a one-time link. Anyone's rejection ends the request (reason required): **Rejected**, final, the others are released and the requester is told. An *any* step is settled by the first approval (the others become *not needed*); an *all* step waits for everyone. When a step is approved the next one starts and its approvers are told **then** (their links do not exist before). After the last step the request is **released exactly like a request that never needed approval**: it becomes Submitted, is routed by the assignment rules, the SLA clock restarts with the waiting time not counted against the team, and the requester (and any assignee) is notified. Withdrawing a waiting request voids every pending approval and link.

**The e-mail link.** `…/approve?token=…`: an unguessable 256-bit token, only its hash stored, bound to one approver and one step, valid 7 days, single use. Opening the page only *reads* (mail scanners open links); a button press decides. Unknown, used, expired and withdrawn tokens all answer the same 410, and lookups are rate-limited per address. The decision is recorded as made "by e-mail link" with the address and a device summary.

**The trail (APR-006).** Every approver gets one row per step; once decided it never changes (who, when, decision, comment, how, from where), plus a timeline entry and an audit row (`approval.approve|reject`, `approval.policy.set|remove`). The requester, staff and the approvers can see it on the ticket. An approver can open the ticket they are asked to decide even if they could not see it otherwise.

**Notifications.** The bell (everyone) lists, counts and marks read — only your own; an id that is not yours looks like it does not exist. Recipients: *assigned to you* (in-app + e-mail), *work started / on hold / closed / cancelled* and *reply* to the requester (reply also by e-mail), *resolved* (in-app + e-mail asking to confirm), a requester's reply to the assignee, an internal note to the assignee only, *reopened* to the assignee, *approval needed / approved / rejected*, and *SLA at risk / breached* to the assignee, the team's lead(s) and up to 10 ticket supervisors (breach also by e-mail). Nobody is notified about their own action. E-mails are queued in the recipient's language and skipped when the person turned e-mail off (except approval requests). Sending is part of the same save as the change that caused it.

## Verification
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16, Testcontainers) | **498 passed, 0 failed** — includes 4 new pure approval-engine tests, the whole workflow matrix extended to the new statuses, and 15 integration tests (policy admin, one-of-two and sequential flows, rejection, self-approval and fallback, roles, withdrawal, the e-mail link incl. single use and expiry, the clock, the bell, who is told what, e-mail preference, SLA breach notice, tenant isolation) |
| `tsc --noEmit`, `eslint` on new files, `next build` | pass |
| Playwright `approvals.spec.ts` (Chromium): an administrator sets a policy from the UI; a request waits, the approver approves in the app, the requester sees the trail, the bell and the notification; the e-mail link rejects without sign-in, needs a reason, works once; axe on the policy screen and drawer, the inbox, the ticket, notifications and the public link page | **3 passed** |
| Full Playwright suite (existing + new specs, one shared database), incl. axe light + dark for the new pages | **137 passed, 0 failed** |
| Frontend `vitest` | still cannot run on Node 20.16 (vitest 5 needs ≥ 22.12) |

Findings fixed on the way: PostgreSQL's `jsonb` re-formats stored JSON, so "nothing changed" detection compared text and always saw a change (it now compares the steps); a rejection also marked the rejecting approver's own row as cancelled (the "release everyone else" query now skips it); after *mark all as read* the bell kept its old count until the next poll (the screen now tells the shell); the public-surface guard test failed on the two new link routes — that is the guard doing its job, and the routes were added to its list on purpose.

## Decisions and known gaps
- **Requester's manager** as an approver (APR-002) needs a manager field on users; until then use named people, roles and the fallback.
- **Delegation / out-of-office, reminders, timeouts, cost-based steps** (APR-004/005, part of APR-001) are not built.
- Steps' approvers are fixed when the request is made; a person who leaves meanwhile cannot approve (their rows stay pending; an administrator withdraws the request or uses another approver of the step).
- Supervisors for SLA notices: the assignee, the team's lead(s), and at most 10 holders of `tickets.assign` (ordered by id) — large organisations should make team leads explicit.
- Not built: web push, WhatsApp/Teams/Slack adapters, digests, per-organisation templates, a dead-letter view for failed mail, unsubscribe links.
- Requests that need approval are visible to their team's agents in the queue as *Pending approval* (they can only cancel them).

## Next
Dashboards (operations and SLA), then attachments and the storage port; later releases add work orders, assets and preventive maintenance.
