# Enterprise Auth & User Management Starter Kit — Documentation

Status: **Phase 2 prototype implemented — awaiting Product Owner review.**
No backend has been implemented. Per the approved workflow, backend work starts only after the prototype is approved.

| Doc | Purpose | Phase |
|---|---|---|
| [00-audit.md](00-audit.md) | Repository baseline, reusable assets, defects | 1 |
| [01-prd.md](01-prd.md) | PRD with requirement IDs, priorities, acceptance mapping | 1 |
| [02-user-flows.md](02-user-flows.md) | End-to-end journeys incl. failure paths | 1 |
| [03-ia-screen-inventory.md](03-ia-screen-inventory.md) | Menu hierarchy, route map, screen × state inventory | 1 |
| [04-decisions-and-assumptions.md](04-decisions-and-assumptions.md) | Pending decisions with recommended defaults, assumptions | 1 |
| [05-prototype-review.md](05-prototype-review.md) | Prototype run guide, coverage, verification evidence, scorecard template | 2 |
| [06-architecture.md](06-architecture.md) | Layers, request lifecycle, module layout, extension points | 3 |
| [07-data-model.md](07-data-model.md) | ERD, tables, constraints, state machines, retention | 3 |
| [08-api-spec.md](08-api-spec.md) | Endpoint contracts, permissions, errors, rate limits, headers | 3 |
| [09-rbac-matrix.md](09-rbac-matrix.md) | Role × permission matrix and authorization invariants | 3 |
| [10-security-and-threat-model.md](10-security-and-threat-model.md) | Security design, STRIDE threat model, ASVS mapping | 3 |
| [11-test-plan.md](11-test-plan.md) | Strategy, test catalogue by requirement, release gate | 3 |
| [12-migration-and-rollback.md](12-migration-and-rollback.md) | Migrations, deployment sequence, rollback, key rotation | 3 |
| [13-adr-dotnet-backend.md](13-adr-dotnet-backend.md) | ADR: backend in ASP.NET Core (.NET 10), hosting & integration impact | 4 |
| [14-phase4-slice1-report.md](14-phase4-slice1-report.md) | Slice 1 (foundation + auth core): what exists, evidence, gaps | 4 |
| [15-phase4-slice2-report.md](15-phase4-slice2-report.md) | Slice 2 (registration, verification, recovery, invitation acceptance) | 4 |
| [16-phase4-slice3-report.md](16-phase4-slice3-report.md) | Slice 3 (MFA: TOTP, recovery codes, second step, policy) | 4 |
| [17-phase4-slice4-report.md](17-phase4-slice4-report.md) | Slice 4 (user & invitation management, hierarchy rule, last-admin guard) | 4 |
| [18-phase4-slice5-report.md](18-phase4-slice5-report.md) | Slice 5 (roles, permission catalog, access matrix) | 4 |
| [19-phase4-slice6-report.md](19-phase4-slice6-report.md) | Slice 6 (sessions, audit/security, dashboards, profile, settings, retention) | 4 |
| [20-phase4-slice7-report.md](20-phase4-slice7-report.md) | Slice 7 (UI cutover to the real API, Playwright journeys/a11y/responsive) | 4 |
| [21-hardening-report.md](21-hardening-report.md) | Hardening: durable e-mail outbox, wider e2e coverage, bell contrast fix | 4 |
| [22-staging-runbook.md](22-staging-runbook.md) | Staging: API Dockerfile, CI workflow, first-admin bootstrap, runbook (nothing deployed) | 4 |
| [23-ga-helpdesk-prd.md](23-ga-helpdesk-prd.md) | GA Helpdesk (enterprise, multi-tenant) PRD: modules, requirements, NFRs, release plan, production-readiness gate | 5 (proposed) |
| [24-ga-helpdesk-slice1-report.md](24-ga-helpdesk-slice1-report.md) | GA Helpdesk R1 slice 1: locations, teams, business calendars + working-time engine; verification status | 5 |
| [25-ga-helpdesk-slice2-report.md](25-ga-helpdesk-slice2-report.md) | GA Helpdesk R1 slice 2: service catalog, versioned dynamic forms (engine + builder), starter GA catalog, requester portal read side; verification | 5 |
| [26-ga-helpdesk-slice3-report.md](26-ga-helpdesk-slice3-report.md) | GA Helpdesk R1 slice 3: ticket lifecycle (state machine, numbering, assignment, comments, auto-close), GA roles, requester screens; verification | 5 |
| [27-ga-helpdesk-slice4-report.md](27-ga-helpdesk-slice4-report.md) | GA Helpdesk R1 slice 4: assignment rules (round robin, least loaded), SLA policies and clocks (pause, escalation, state), verification | 5 |
| [28-ga-helpdesk-slice5-report.md](28-ga-helpdesk-slice5-report.md) | GA Helpdesk R1 slice 5: approvals (policies, steps, one-time e-mail link, trail) and notifications (bell + e-mail); verification | 5 |
| [29-ga-helpdesk-slice6-report.md](29-ga-helpdesk-slice6-report.md) | GA Helpdesk R1 slice 6: operations dashboard and SLA report (RPT-001/002), definitions of every number; verification | 5 |
| [30-ga-helpdesk-slice7-report.md](30-ga-helpdesk-slice7-report.md) | GA Helpdesk R1 slice 7: attachments, storage port, the file question; limits, security decisions, verification | 5 |
| [31-ga-helpdesk-slice8-report.md](31-ga-helpdesk-slice8-report.md) | GA Helpdesk R1 slice 8: go-live checklist and quick start, ClamAV scanner and Production gates, GA Manager role review, demo data; verification | 5 |
| [32-ga-helpdesk-slice9-report.md](32-ga-helpdesk-slice9-report.md) | GA Helpdesk R1 slice 9: queue productivity — more filters, bulk actions, saved views (TKT-011/015); verification | 5 |
| [33-ga-helpdesk-slice10-report.md](33-ga-helpdesk-slice10-report.md) | GA Helpdesk R1 slice 10: approval delegation and hand-over, reporting line and the requester's-manager step (APR-002/004); verification | 5 |
| [34-ga-helpdesk-slice11-report.md](34-ga-helpdesk-slice11-report.md) | GA Helpdesk R1 slice 11: per-person notification preferences by kind and channel (NTF-002); what already existed for delivery tracking (NTF-004); verification | 5 |
| [35-ga-helpdesk-slice12-report.md](35-ga-helpdesk-slice12-report.md) | GA Helpdesk R1 slice 12: performance at 500,000 tickets — load seed and load test, indexes, literal search, database-side reports, query plans; measured results against the PRD budgets | 5 |
| [36-ga-helpdesk-slice13-report.md](36-ga-helpdesk-slice13-report.md) | GA Helpdesk R1 slice 13: data retention per organisation, anonymise or delete, legal holds, purge worker, privacy notice and data-subject export (ADM-006); verification | 5 |
| [37-ga-helpdesk-slice14-report.md](37-ga-helpdesk-slice14-report.md) | GA Helpdesk R1 slice 14: backups that include the files, restore drill script, files-versus-database check and cleanup; verification | 5 |
| [38-ga-helpdesk-slice15-report.md](38-ga-helpdesk-slice15-report.md) | GA Helpdesk R1 slice 15: in-app help for requesters, agents and administrators, in English and Bahasa Indonesia; search, contextual Help buttons, dead-link test | 5 |
| [39-ga-helpdesk-slice16-report.md](39-ga-helpdesk-slice16-report.md) | GA Helpdesk R1 slice 16: per-organisation e-mail wording (subject start, closing text in both languages, sign-off name) with preview; what of NTF-002 stays open; where R1 stands | 5 |
| [40-ga-helpdesk-r2-plan.md](40-ga-helpdesk-r2-plan.md) | GA Helpdesk release R2 (Operations): the slices, their order and dependencies | 5 |
| [41-ga-helpdesk-r2-slice1-report.md](41-ga-helpdesk-r2-slice1-report.md) | GA Helpdesk R2 slice 1: asset register — types with their own questions, lifecycle trail, link requests to assets, CSV export/import with a validation report; verification | 5 |
| [42-ga-helpdesk-r2-slice2-report.md](42-ga-helpdesk-r2-slice2-report.md) | GA Helpdesk R2 slice 2: QR labels, scan page and reporting a problem from an asset, the "fails often" hint, warranty reminders; verification | 5 |
| [43-ga-helpdesk-r2-slice3-report.md](43-ga-helpdesk-r2-slice3-report.md) | GA Helpdesk R2 slice 3: work orders on tickets — checklists and templates, photo evidence, time, parts and costs, holding and resolving the ticket; verification | 5 |
| [44-ga-helpdesk-r2-slice4-report.md](44-ga-helpdesk-r2-slice4-report.md) | GA Helpdesk R2 slice 4: vendors — directory, contacts and places, contracts with expiry reminders, documents, blocking, work orders given to a vendor with a PO, first scorecard; verification | 5 |
| [45-ga-helpdesk-r2-slice5-report.md](45-ga-helpdesk-r2-slice5-report.md) | GA Helpdesk R2 slice 5: preventive maintenance — plans by asset, type or place, scheduler with lead time, catch-up and no duplicates, compliance report; verification | 5 |
| [46-ga-helpdesk-r2-slice6-report.md](46-ga-helpdesk-r2-slice6-report.md) | GA Helpdesk R2 slice 6: satisfaction (CSAT) — rating after resolution, low-score alert, report per agent/team/site/vendor, vendor scorecard rating; verification | 5 |
| [47-ga-helpdesk-r2-slice7-report.md](47-ga-helpdesk-r2-slice7-report.md) | GA Helpdesk R2 slice 7: knowledge base — versioned articles, publishing, staff-only articles, files, helpful votes, suggestions while asking and deflection; verification | 5 |
| [48-ga-helpdesk-r2-slice8-report.md](48-ga-helpdesk-r2-slice8-report.md) | GA Helpdesk R2 slice 8: cost and budget — currency and labour rate, vendor invoices, cost centres, cost report with CSV export, budgets with 80/100 % alerts; verification | 5 |
| [49-ga-helpdesk-r2-slice9-report.md](49-ga-helpdesk-r2-slice9-report.md) | GA Helpdesk R2 slice 9: e-mail to ticket — secret webhook for raw MIME, new requests, replies threaded by ticket number, loop and abuse protection, message log; verification | 5 |
| [50-ga-helpdesk-r2-slice10-report.md](50-ga-helpdesk-r2-slice10-report.md) | GA Helpdesk R2 slice 10: API keys (scoped, hashed, acting as their creator) and the public API v1 with idempotent creates; signed, retried webhooks with SSRF protection; public API reference; verification | 5 |
| [51-ga-helpdesk-r2-slice11-report.md](51-ga-helpdesk-r2-slice11-report.md) | GA Helpdesk R2 slice 11: vendor portal — Vendor role, portal people per vendor, their company's work orders only, server-side allow-list; R2 status; verification | 5 |
| [52-ga-helpdesk-ui-review.md](52-ga-helpdesk-ui-review.md) | GA Helpdesk UI review of every screen: template identity left over, missing per-module analytics, layout issues; recommended plan | 5 |
| [53-ga-helpdesk-ui-redesign-report.md](53-ga-helpdesk-ui-redesign-report.md) | GA Helpdesk UI redesign: helpdesk identity, menu by job, role-based homes, KPI strip + charts on every module (5 summary endpoints), ticket page in two columns, Rupiah, demo data for all modules; verification | 5 |
| [54-ga-helpdesk-insights-periods-report.md](54-ga-helpdesk-insights-periods-report.md) | GA Helpdesk: period picker on the work-order, vendor and maintenance summaries, time to complete by category, satisfaction trend, dark-mode review; verification | 5 |
| [55-ga-helpdesk-r3-plan.md](55-ga-helpdesk-r3-plan.md) | GA Helpdesk release R3 (Facilities suite) plan: 11 slices (bookings, vehicles, visitors, mail, inventory, supplies, scheduled reports, directory sync, chat channels, offline queue), permissions and up-front decisions | 5 |
| [56-ga-helpdesk-r3-slice1-report.md](56-ga-helpdesk-r3-slice1-report.md) | GA Helpdesk R3 slice 1: bookable resources and room/vehicle/parking/equipment booking, overlap refused by a database exclusion constraint, day and week timeline, privacy of others' bookings; verification | 5 |
| [57-ga-helpdesk-r3-slice2-report.md](57-ga-helpdesk-r3-slice2-report.md) | GA Helpdesk R3 slice 2: booking approval, check-in with no-shows released, reminders, repeating bookings, private calendar feed (ICS); verification | 5 |
| [58-ga-helpdesk-r3-slice3-report.md](58-ga-helpdesk-r3-slice3-report.md) | GA Helpdesk R3 slice 3: vehicle trips (driver, odometer, costs, return checklist, damage), vehicle documents and driving licences with reminders, utilisation report, trip costs in the cost report; verification | 5 |
| [59-ga-helpdesk-r3-slice4-report.md](59-ga-helpdesk-r3-slice4-report.md) | GA Helpdesk R3 slice 4: visitors (pre-registration, reception check-in/out with badge, ID digits and acknowledgement, block list, host told on arrival, no-shows, retention, visitor report), Receptionist role; verification | 5 |
| [60-ga-helpdesk-r3-slice5-report.md](60-ga-helpdesk-r3-slice5-report.md) | GA Helpdesk R3 slice 5: mail and courier log (incoming with recipient told, reminded and collection recorded; outgoing with courier and waybill), mailroom KPIs, retention; verification | 5 |
| [61-ga-helpdesk-r3-slice6-report.md](61-ga-helpdesk-r3-slice6-report.md) | GA Helpdesk R3 slice 6: inventory (items, per-place stock kept by a ledger that never goes below zero, receipts, issues, adjustments, transfers, low-stock alerts, reorder list, stock value, stock-takes); verification | 5 |
| [62-ga-helpdesk-r3-slice7-report.md](62-ga-helpdesk-r3-slice7-report.md) | GA Helpdesk R3 slice 7: supply requests (cart, manager approval above an amount, issuing from the ledger with partial fulfilment and back-orders, close and cancel) and work-order parts drawn from stock; verification | 5 |
| [63-ga-helpdesk-r3-slice8-report.md](63-ga-helpdesk-r3-slice8-report.md) | GA Helpdesk R3 slice 8: report exports (seven reports as CSV or hand-written XLSX, formula-safe, row-limited, audited) and scheduled reports e-mailed per recipient with their own access; verification | 5 |
| [64-ga-helpdesk-r3-slice9-report.md](64-ga-helpdesk-r3-slice9-report.md) | GA Helpdesk R3 slice 9: directory (SSO and SCIM groups mapped to roles, never Super Admin; SCIM 2.0 Users and Groups over API keys; HR CSV sync with preview that updates employee data and suspends leavers); verification | 5 |
| [65-ga-helpdesk-r3-slice10-report.md](65-ga-helpdesk-r3-slice10-report.md) | GA Helpdesk R3 slice 10: WhatsApp, Slack and Teams channels behind the notification port (encrypted credentials and addresses, opt-in with WhatsApp code, outbox with retries, e-mail fallback); verification | 5 |
| [66-adr-ga-07-offline-field-mode.md](66-adr-ga-07-offline-field-mode.md) | ADR-GA-07: offline field mode for technicians (IndexedDB, not the service worker; own work orders only; idempotent replays; conflicts shown) | 4 |
| [67-ga-helpdesk-r3-slice11-report.md](67-ga-helpdesk-r3-slice11-report.md) | GA Helpdesk R3 slice 11: field mode (snapshot of my open work orders, offline changes queued and replayed with idempotency keys, conflicts); verification | 5 |
| [68-helpdesk-user-manual.md](68-helpdesk-user-manual.md) | Panduan Pengguna Annaba Helpdesk (bahasa Indonesia), per peran, dengan gambar layar dan alur proses; PDF di `manual/Panduan-Pengguna-Annaba-Helpdesk.pdf` (`npm run manual:screens`, `npm run manual:pdf`) | semua pengguna |

## Phase tracker
| Phase | State |
|---|---|
| 1 Discovery & Requirements | Done — decisions D-01…D-14 approved by PO as recommended defaults (D-08 density reference still outstanding) |
| 2 UX & Design (interactive prototype, scorecard, PO approval) | **Prototype built — awaiting PO review/approval and scorecard** |
| 3 Technical design (ERD, API, threat model, migration plan) | **Drafted (docs 06–12) — awaiting PO approval**; open items listed in docs 10 §5 |
| 4 Implementation | **Started (PO: "Setuju, mulai Phase 4"; backend = .NET per ADR-013).** Slice 1 (foundation + auth core) **done & verified (70 .NET tests)**; **slices 2–6 done & verified (257 .NET tests total)**; slice 7 (UI cutover + Playwright) not started; UI still on prototype engine |
| 5 Validation | Not started |
| 6 Delivery | Not started |

Deliverables not yet produced: UI/UX prototype, architecture doc, ERD, API spec, RBAC matrix, threat model, test plan/evidence, setup & deployment runbooks, final report.
