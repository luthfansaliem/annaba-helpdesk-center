# 23 — GA Helpdesk: Product Requirements Document (PRD)

Product: **GA Helpdesk** — an enterprise, multi-tenant General Affairs (GA) service-desk platform, built on top of this repository's Identity & Access foundation.
Version: 0.1 (draft for PO review). Status: **proposed — nothing in this document is implemented yet.**
Priority: **P0** = required for the first production release (MVP), **P1** = required for general availability (GA), **P2** = post-GA roadmap / extension point.

> Terminology: "GA" = *General Affairs* (Indonesian: *Umum / Rumah Tangga Kantor*), the function that runs office facilities, vehicles, supplies and administrative services. It is not "general availability" except where written as "release GA" in §14.

---

## 1. Background and problem

In most companies GA requests arrive through WhatsApp groups, e-mail, paper forms and walk-ups: AC not cooling, toilet blocked, pool-car booking, meeting-room set-up, ATK (stationery) restock, visitor arrival, courier, ID card, parking. The result:

- No single queue, so requests are lost, duplicated or handled out of order.
- No SLA, no accountability, no evidence of who did what and when.
- Approvals (budget, vehicle, purchase) happen in chat and cannot be audited.
- Facility managers cannot see recurring failures, vendor performance, cost per site or cost per department.
- Each company reinvents the same process; multi-site groups have no consistent view.

## 2. Vision and goals

**Vision:** one place where any employee can ask GA for anything, GA can run it with clear ownership and SLAs, and management can see cost, performance and risk — configurable per company without code changes.

| # | Goal | Measure |
|---|---|---|
| G1 | Replace chat/e-mail intake with a single, trackable request flow | ≥ 90 % of GA requests created in-app within 3 months of go-live |
| G2 | Make service levels visible and enforced | SLA clock on every ticket; breach alerts; SLA attainment report |
| G3 | Make it usable by **any** company without code changes | A new tenant is configured (sites, catalog, SLAs, workflows) from the UI in < 1 day |
| G4 | Prove accountability | Every state change, approval, assignment and cost entry is audited and exportable |
| G5 | Be safe to run as a multi-tenant SaaS or single-company install | Tenant isolation tests pass; passes the production-readiness checklist in §14 |

**Non-goals (v1):** full IT service management (ITIL change/problem/CMDB — use ITSM tools), HR leave/payroll, accounting/ERP general ledger, full CAFM/BMS integration (sensor data), procurement beyond GA purchase requests, payment processing.

## 3. Users and personas

| Persona | Role in system | Key needs |
|---|---|---|
| **Requester (Employee)** | Creates requests, books resources, tracks status, rates service | Fast intake (≤ 3 taps), clear status, notifications, mobile/QR |
| **Approver (Line manager / budget owner)** | Approves/rejects requests | One-tap approve from e-mail/app, see cost and context, delegate when away |
| **GA Agent / Technician** | Works tickets and work orders | Own queue, mobile-first, checklists, photos, parts used, time spent |
| **GA Supervisor / Team Lead** | Assigns, balances load, handles escalations | Board/queue views, SLA risk, reassign in bulk |
| **Facility / GA Manager** | Owns service quality and cost | Dashboards, vendor performance, preventive-maintenance compliance, budget |
| **Vendor user (external)** | Receives and updates assigned work orders | Scoped access to *only* their work orders; no internal data |
| **Receptionist / Security** | Visitors, parking, mail & courier | Fast visitor pre-registration/check-in, package log |
| **Tenant Admin** | Configures the company's helpdesk | Catalog, forms, SLAs, workflows, sites, roles, integrations — no developer needed |
| **Platform Operator** | Runs the SaaS | Tenant provisioning, plan limits, support mode (existing), health |
| **Auditor / Finance** | Read-only review, cost export | Audit log, cost reports, immutable history |

## 4. Scope overview

Modules (each is a bounded context in the backend and a route group in the UI):

1. **Service catalog & intake** — categories, services, dynamic forms, portal, QR-code intake
2. **Ticketing & work orders** — lifecycle, assignment, SLA, comments, attachments, escalation
3. **Approvals** — configurable multi-step approval chains with delegation
4. **Sites, buildings, floors, rooms (locations)** — hierarchy used by every module
5. **Asset & equipment register** — GA assets, QR labels, warranty, service history
6. **Preventive maintenance (PM)** — recurring schedules that generate work orders
7. **Inventory & office supplies (ATK/pantry)** — stock, requests, reorder levels
8. **Bookings** — meeting rooms, pool vehicles, parking, shared equipment
9. **Visitors, mail & courier** — pre-registration, check-in/out, package log
10. **Vendors & contracts** — directory, SLAs, external work orders, cost
11. **Cost, budget & chargeback** — cost per ticket/site/department, budgets
12. **Knowledge base & self-service** — FAQ/articles, deflection
13. **Notifications & integrations** — e-mail, in-app, push, WhatsApp/Teams/Slack, webhooks, API, SSO, HRIS
14. **Reporting & analytics** — operational, SLA, cost, vendor, asset
15. **Administration** — tenant configuration, business calendars, roles, retention (builds on existing IAM)

## 5. Functional requirements

IDs are stable and referenced by tests and the release checklist.

### 5.1 Service catalog and intake (CAT)
| ID | Requirement | Pri |
|---|---|---|
| CAT-001 | Hierarchical **categories** and **services** per tenant (e.g. *Facilities → AC repair*, *Transport → Pool car*), each with description, icon, owning team, default priority, SLA policy, visibility (who may request), active/archived | P0 |
| CAT-002 | **Request templates**: starter catalog for GA (maintenance, cleaning, ATK, vehicle, room, visitor, courier, ID card, event set-up, relocation) installed on tenant creation; fully editable | P0 |
| CAT-003 | **Dynamic form builder** per service: text, long text, number, date/time, single/multi select, location picker, asset picker, user picker, file/photo, yes/no, conditional visibility, required rules, validation; versioned (in-flight tickets keep the version they were created with) | P0 |
| CAT-004 | Requester portal: search, browse catalog, "my requests", "requests for me" (on behalf of), draft save | P0 |
| CAT-005 | **QR-code intake**: print a QR per room/asset/floor; scanning opens the request form pre-filled with that location/asset (works for authenticated users; optional anonymous report with captcha when the tenant enables it) | P1 |
| CAT-006 | E-mail-to-ticket (per-tenant inbound address), reply threading, loop/auto-reply detection | P1 |
| CAT-007 | Duplicate hint: when a new ticket matches an open ticket for the same location/asset/category, suggest "me too / follow" instead of creating a duplicate | P1 |
| CAT-008 | Bulk/recurring request creation (e.g. weekly pantry restock) via schedule | P2 |

### 5.2 Ticketing and work orders (TKT)
| ID | Requirement | Pri |
|---|---|---|
| TKT-001 | Ticket fields: number (per-tenant sequence, e.g. `GA-2026-000123`), service, subject, description, requester, on-behalf-of, location, asset, priority, impact/urgency, status, assignee, team, due date, tags, custom form answers, attachments, created/updated/closed times | P0 |
| TKT-002 | **Lifecycle** (state machine, enforced server-side): `Draft → Submitted → PendingApproval → Approved → Assigned → InProgress → OnHold(reason) → Resolved → Closed`, plus `Rejected`, `Cancelled`, `Reopened`. Illegal transitions are rejected; each transition is audited with actor and reason. Tenant admins can rename statuses and map them to these system states | P0 |
| TKT-003 | Assignment: manual, round-robin, least-loaded, by location/skill/shift; **assignment rules** evaluated in order with first-match; manual override always allowed with permission | P0 |
| TKT-004 | Comments: public (visible to requester) vs internal note; @mentions; attachments; edit history; comments by e-mail reply | P0 |
| TKT-005 | Attachments: images/PDF/office docs; size and type allow-list; antivirus scan hook; stored outside the DB behind a storage port; signed, expiring download URLs; EXIF GPS stripped optionally | P0 |
| TKT-006 | **Work orders**: a ticket may spawn child work orders (internal or vendor) with their own assignee, checklist, parts, labour time and cost; ticket auto-resolves when all required children complete (configurable) | P1 |
| TKT-007 | **Checklists** per service/work order (required items block resolution), photo-before/after evidence | P1 |
| TKT-008 | Resolution: resolution code + notes required; requester confirms or reopens within a configurable window; auto-close after N days | P0 |
| TKT-009 | **Satisfaction (CSAT)**: 1–5 rating + comment after resolution; low scores raise an alert; reportable per agent/team/site/vendor | P1 |
| TKT-010 | Merge, split, link (related/duplicate/parent-child) tickets | P1 |
| TKT-011 | Saved views and filters (status, assignee, team, site, SLA state, priority, date, tag), list and board (kanban) layouts, bulk actions (assign, change status/priority, add tag) with permission and per-item audit | P0 |
| TKT-012 | Watchers/followers; requester and watchers receive configured notifications | P1 |
| TKT-013 | Canned responses/macros with variables | P1 |
| TKT-014 | Time tracking (manual + timer) per agent per ticket/work order | P1 |
| TKT-015 | Global search over ticket number, subject, description, requester, asset (full-text, tenant-scoped, permission-filtered) | P0 |

### 5.3 SLA, priority and calendars (SLA)
| ID | Requirement | Pri |
|---|---|---|
| SLA-001 | **Priority matrix** (impact × urgency → priority P1–P4) configurable per tenant | P0 |
| SLA-002 | **SLA policies**: first-response target and resolution target per priority and per service/site; selected by first-matching rule | P0 |
| SLA-003 | **Business calendars**: working hours per day, holidays, time zone, per site; SLA clock counts only working time; multiple calendars (e.g. 24/7 for security, office hours for supplies) | P0 |
| SLA-004 | **Clock rules**: pause while `OnHold` with a pausing reason or `PendingApproval`; resume on exit; recalculated on priority change; the stored timeline is the source of truth for reports | P0 |
| SLA-005 | **Escalation**: at configurable thresholds (e.g. 50 %, 80 %, breach) notify assignee/supervisor/manager and optionally reassign/raise priority; breach recorded permanently | P0 |
| SLA-006 | SLA state shown on every ticket: *On track / At risk / Breached / Paused / Met*; sortable and filterable | P0 |
| SLA-007 | Operational-level agreements for vendors (vendor work-order targets) | P1 |

### 5.4 Approvals (APR)
| ID | Requirement | Pri |
|---|---|---|
| APR-001 | Approval **policies** per service: none, requester's manager, specific user/role, cost-threshold based, sequential or parallel steps, "any/all of group" | P0 |
| APR-002 | Manager resolution from the user directory (manager field, HRIS sync, or manual); fallback approver when missing | P0 |
| APR-003 | Approve/reject with mandatory comment on reject; approve from the app and via **signed one-time e-mail link** (no login required, expiring, single-use, bound to approver and step) | P0 |
| APR-004 | **Delegation** (out-of-office, date range) and reassignment; approver can never approve their own request | P0 |
| APR-005 | Approval reminders and auto-escalation after N hours; optional auto-approve/auto-reject on timeout (tenant choice) | P1 |
| APR-006 | Immutable approval trail: who, when, decision, comment, IP/device summary | P0 |

### 5.5 Locations (LOC)
| ID | Requirement | Pri |
|---|---|---|
| LOC-001 | Hierarchy: Region → Site → Building → Floor → Room/Zone, unlimited per tenant, with code, address, time zone, calendar, capacity, manager | P0 |
| LOC-002 | Users are associated to a primary site (and optionally additional sites); roles can be scoped to sites (see §8) | P0 |
| LOC-003 | Import/export locations (CSV); floor plans (image) with optional asset/room pins | P1 |

### 5.6 Assets and equipment (AST)
| ID | Requirement | Pri |
|---|---|---|
| AST-001 | Asset register: type, name, tag/serial, location, owner/department, status (in use, in repair, stored, retired), purchase date/cost, warranty end, vendor, custom fields | P0 |
| AST-002 | QR/barcode labels (print sheets), scan to open asset, report issue, or log check | P1 |
| AST-003 | Service history: every ticket/work order/PM linked to the asset; failure frequency and cost per asset; "replace vs repair" indicator | P1 |
| AST-004 | Asset lifecycle events (assign, move, retire) audited; depreciation fields informational only (no accounting) | P1 |
| AST-005 | Warranty and contract expiry reminders | P1 |
| AST-006 | CSV import/export with validation report | P0 |

### 5.7 Preventive maintenance (PM)
| ID | Requirement | Pri |
|---|---|---|
| PM-001 | PM schedules: time-based (daily/weekly/monthly/custom RRULE), by asset, asset type or location; checklist; default assignee/team/vendor | P1 |
| PM-002 | Scheduler generates work orders ahead of due date (lead time); missed generation is detected and caught up; idempotent (no duplicates on retry) | P1 |
| PM-003 | PM compliance report (planned vs done on time) per site/team/vendor | P1 |
| PM-004 | Meter-based triggers (km for vehicles, running hours) | P2 |

### 5.8 Inventory and supplies (INV)
| ID | Requirement | Pri |
|---|---|---|
| INV-001 | Item catalog (ATK, pantry, cleaning supplies, uniforms), unit, SKU, category, min/reorder level, per-site stock | P1 |
| INV-002 | Supply request (cart) with approval policy and budget check; fulfilment decrements stock; partial fulfilment and back-order | P1 |
| INV-003 | Stock in (receipt), stock out, adjustment, transfer between sites — every movement is a ledger entry (no direct edits of quantity) | P1 |
| INV-004 | Low-stock alerts and suggested purchase list; stock-take (cycle count) with variance report | P1 |
| INV-005 | Parts consumed on a work order are drawn from inventory and costed | P1 |

### 5.9 Bookings (BKG)
| ID | Requirement | Pri |
|---|---|---|
| BKG-001 | **Bookable resources**: meeting rooms, pool vehicles, parking slots, shared equipment — each with location, capacity, features, availability hours, buffer time, approval rule, allowed users/groups | P1 |
| BKG-002 | Booking with **conflict prevention enforced by the database** (exclusion constraint on resource + time range, per tenant) so concurrent requests cannot double-book | P1 |
| BKG-003 | Calendar and availability views (day/week), search by capacity/feature/time, recurring bookings with exceptions | P1 |
| BKG-004 | Vehicle bookings: driver (own or assigned), purpose, destination, passengers, odometer out/in, fuel and toll cost, damage notes, return checklist; licence and STNK/insurance expiry tracking | P1 |
| BKG-005 | Check-in with auto-release of no-show rooms; reminders; cancellation rules; calendar (ICS) feed and optional Microsoft 365/Google calendar sync | P1 |
| BKG-006 | Room set-up/catering add-ons create linked tickets | P2 |

### 5.10 Visitors, mail and courier (VIS)
| ID | Requirement | Pri |
|---|---|---|
| VIS-001 | Visitor pre-registration by employee: name, company, host, purpose, time window, vehicle plate; host and reception notified | P1 |
| VIS-002 | Reception check-in/out with badge number, photo (optional), ID capture rules per tenant, NDA/acknowledgement text; blacklist check | P1 |
| VIS-003 | Visitor QR pass sent by e-mail/WhatsApp; scan at gate | P2 |
| VIS-004 | Incoming/outgoing mail and package log: carrier, waybill, recipient, status (received → notified → collected), signature/photo proof; recipient notified | P1 |
| VIS-005 | Visitor and package data retention and anonymisation after a configurable period | P1 |

### 5.11 Vendors and contracts (VND)
| ID | Requirement | Pri |
|---|---|---|
| VND-001 | Vendor directory: contacts, services, sites covered, contract dates/value, documents, status (active/blocked) | P1 |
| VND-002 | Vendor portal accounts with **scoped** access: only work orders assigned to that vendor, accept/decline, update status, upload evidence, submit quote/cost | P1 |
| VND-003 | Quote approval before work above a threshold; PO number reference (reference only, no ERP posting) | P1 |
| VND-004 | Vendor scorecard: SLA attainment, rework rate, CSAT, cost | P1 |
| VND-005 | Contract expiry reminders | P1 |

### 5.12 Cost, budget and chargeback (CST)
| ID | Requirement | Pri |
|---|---|---|
| CST-001 | Cost lines on tickets/work orders: labour (rate × time), parts, vendor invoice, other; currency per tenant (default IDR; multi-currency with stored rate at entry time is P2) | P1 |
| CST-002 | Cost centers and departments; chargeback by department/site/service; monthly cost report export | P1 |
| CST-003 | Budgets per cost center/site/category with consumption tracking and threshold alerts; approval policy can require budget availability | P1 |
| CST-004 | Money stored as integer minor units/decimal(18,2); no floating point; rounding rules documented | P0 |

### 5.13 Knowledge base (KB)
| ID | Requirement | Pri |
|---|---|---|
| KB-001 | Articles (rich text, images), categories, visibility (all staff / agents only / public), versioning, review dates, helpful/not-helpful | P1 |
| KB-002 | Suggest articles while typing a request; show "was this helpful" deflection metric | P1 |
| KB-003 | Policy documents and SOP attachments linked to services | P1 |

### 5.14 Notifications and integrations (NTF / INT)
| ID | Requirement | Pri |
|---|---|---|
| NTF-001 | Event-driven notifications (created, assigned, commented, status change, approval needed, SLA at risk/breach, booking reminder, low stock, etc.) through channels: in-app, e-mail (existing outbox), web push | P0 |
| NTF-002 | Per-user preferences and per-tenant templates (English and Bahasa Indonesia, variables), digest option | P0 |
| NTF-003 | WhatsApp Business, Microsoft Teams, Slack channel adapters via the notification port (tenant-supplied credentials, encrypted) | P1 |
| NTF-004 | Delivery tracking, retry with back-off, dead-letter view, unsubscribe where legally required | P0 |
| INT-001 | **Public REST API** with per-tenant API keys/OAuth client-credentials, scopes, rate limits, OpenAPI spec, idempotency keys on create | P1 |
| INT-002 | **Webhooks** (HMAC-signed, retried, replay-protected) for ticket, approval, booking, asset events | P1 |
| INT-003 | SSO (OIDC — already in the base) with group-to-role mapping; SCIM or scheduled directory/HRIS sync for users, departments, managers, sites | P1 |
| INT-004 | Calendar (ICS feed; Microsoft Graph/Google) and chat-ops (create ticket from Teams/Slack) | P2 |
| INT-005 | Data import/export: CSV for users, locations, assets, items; full tenant export (JSON/CSV) | P1 |

### 5.15 Reporting and analytics (RPT)
| ID | Requirement | Pri |
|---|---|---|
| RPT-001 | **Operations dashboard** (role-aware): open/overdue/at-risk tickets, backlog by team/site, created vs resolved, average first-response and resolution time, aging | P0 |
| RPT-002 | **SLA report**: attainment by priority/team/site/service/vendor, breach list with reasons | P0 |
| RPT-003 | **Cost report**: by department, site, category, vendor, month; budget vs actual | P1 |
| RPT-004 | **Asset report**: top failing assets/types, repair vs replace, warranty expiry; **PM compliance** | P1 |
| RPT-005 | **Utilisation reports**: room/vehicle utilisation, no-shows; visitor volume; inventory turnover | P1 |
| RPT-006 | **CSAT and agent performance** (own data only for agents; team data for supervisors) | P1 |
| RPT-007 | Export CSV/XLSX/PDF; scheduled e-mailed reports; all exports permission-checked, row-limited, audited | P1 |
| RPT-008 | Reports read from query-optimised views/aggregates and never degrade transactional performance (read replica or materialised rollups when volume requires) | P1 |

### 5.16 Administration and configuration (ADM)
Reuses and extends the existing IAM module (users, roles, permissions, invitations, access requests/reviews, audit, security events, tenant settings, support mode, account closure, data export).

| ID | Requirement | Pri |
|---|---|---|
| ADM-001 | **Tenant onboarding wizard**: company profile, time zone, languages, first site(s), calendar, template catalog, priority matrix and SLAs, invite admins | P0 |
| ADM-002 | Configuration screens for: catalog and forms, SLA/calendars, assignment rules, approval policies, status labels, notification templates, branding (logo, colours, domain) — all versioned and audited | P0 |
| ADM-003 | **Teams/queues**: agents grouped by team with skills, working shifts and capacity; team ↔ service ↔ site mapping | P0 |
| ADM-004 | Organisation directory: departments, cost centers, manager relationship, employee ID; import/sync | P0 |
| ADM-005 | Feature toggles per tenant (modules on/off), plan limits (users, storage, API calls) enforced server-side | P1 |
| ADM-006 | Data retention policies per data class (tickets, attachments, visitors, audit); legal hold; purge jobs | P1 |
| ADM-007 | Platform operator console: tenant lifecycle (create/suspend/delete), usage and health, support mode (existing, with consent and full audit) | P1 |
| ADM-008 | Configuration export/import between tenants (sandbox → production) | P2 |

## 6. Cross-cutting product requirements

### 6.1 Multi-tenancy
- Every tenant-owned row carries `tenant_id`; the existing tenant resolution in `RequestContext` is reused.
- Isolation is enforced in **three layers**: application-level tenant filter on every query (EF global filter), PostgreSQL **row-level security** keyed on a per-request setting as defence-in-depth, and automated tests that attempt cross-tenant reads/writes on every endpoint (deny by default, as the existing authorization coverage test does).
- Per-tenant: settings, branding, languages, calendars, SLA, catalog, integrations, encryption of integration secrets, storage prefix, rate limits and plan limits.
- Tenant data export and deletion on contract end, verified by a test.

### 6.2 Authorization (RBAC + scope)
- Permission catalog `resource.action` extended with GA permissions (see §8); deny by default; server-side only.
- **Scope** is a second dimension: a role assignment may be limited to sites, teams, or "own". Example: *Site GA Supervisor — Jakarta* can assign tickets only for Jakarta.
- Vendors and requesters only ever see their own records via scoped queries; internal notes, costs and other requesters' data are never serialized to them (DTOs per audience, not field hiding in the UI).
- No privilege escalation rules from the base (cannot grant what you do not hold) continue to apply.

### 6.3 Audit and evidence
- Append-only audit written in the same transaction as the change (existing pattern) for: ticket state/assignee/priority/SLA changes, approvals, cost entries, stock movements, booking changes, configuration changes, exports, and all admin actions.
- Immutable timeline per ticket (system events and human comments interleaved).

### 6.4 Internationalisation and localisation
- English and Bahasa Indonesia at launch (existing `t()` mechanism); additional locales by dictionary. Tenant and user locale, time zone, date/number/currency formats; all SLA and business-calendar maths is time-zone aware and tested across DST and month boundaries.

### 6.5 Accessibility and UX
- WCAG 2.2 AA for the requester portal and agent workspace (existing axe-core e2e checks extended to new screens); keyboard-operable lists/boards; light/dark; responsive.
- **Mobile-first for technicians and requesters** via installable PWA: camera for photos/QR scan, push notifications. Offline: read-only cached "my work orders" and **queued** status updates/photos that sync with conflict handling (P1; the service-worker rule in doc 06 §10 is reviewed and revised in a dedicated security review before this ships, because it currently forbids caching API responses).

### 6.6 Reliability of background work
- All scheduled and asynchronous work (SLA timers, PM generation, reminders, notifications, report schedules, webhooks) runs through a **durable job system** with idempotency, retries/back-off, dead-letter, and visibility in the admin UI; no in-memory timers.

## 7. Key workflows

### 7.1 Standard request
`Requester submits → (approval policy?) → auto-assignment rules → agent works → (work orders / vendor?) → resolve with code → requester confirms/CSAT → auto-close`. At every step: SLA clock updated, notifications sent, audit written.

### 7.2 Approval chain
`Submitted → step 1 (line manager) → step 2 (budget owner if cost > threshold) → Approved → continues to assignment`; reject anywhere → `Rejected` with reason and notification; timeout → reminder → escalate/delegate.

### 7.3 Preventive maintenance
`Scheduler (daily) → generate work orders T-n days → assign by rule → technician completes checklist with photos → asset history updated → compliance report`.

### 7.4 Room / vehicle booking
`Search availability → request → (approval if required) → confirmed (DB prevents overlap) → reminder → check-in/out → release no-show → utilisation report`.

### 7.5 Supply request
`Cart → approval/budget check → stock reserved → picked/issued → stock ledger entry → low-stock alert → purchase suggestion`.

## 8. Roles and permission catalog (additions)

System roles seeded per tenant (editable copies; the system definitions stay protected): **Tenant Admin, GA Manager, GA Supervisor, GA Agent, Approver, Requester, Receptionist, Vendor, Auditor** (+ existing IAM roles).

New permission groups (extend `Permissions.cs`; each endpoint declares exactly one):

| Module | Permissions |
|---|---|
| Tickets | `tickets.read.own`, `tickets.read.team`, `tickets.read.all`, `tickets.create`, `tickets.update`, `tickets.assign`, `tickets.transition`, `tickets.comment.internal`, `tickets.delete` (soft), `tickets.export` |
| Work orders | `workorders.read`, `workorders.manage`, `workorders.complete`, `workorders.cost.read`, `workorders.cost.write` |
| Catalog / config | `catalog.read`, `catalog.manage`, `forms.manage`, `sla.manage`, `calendars.manage`, `rules.manage`, `approvals.policy.manage`, `teams.manage`, `templates.manage` |
| Approvals | `approvals.decide`, `approvals.delegate`, `approvals.override` |
| Locations / assets | `locations.read`, `locations.manage`, `assets.read`, `assets.manage`, `assets.import` |
| PM | `pm.read`, `pm.manage` |
| Inventory | `inventory.read`, `inventory.manage`, `inventory.adjust`, `supplies.request`, `supplies.fulfil` |
| Bookings | `bookings.create`, `bookings.read.all`, `bookings.manage`, `resources.manage` |
| Visitors / mail | `visitors.preregister`, `visitors.checkin`, `visitors.read.all`, `mail.manage` |
| Vendors / cost | `vendors.read`, `vendors.manage`, `budgets.read`, `budgets.manage`, `cost.read` |
| KB | `kb.read`, `kb.write`, `kb.publish` |
| Reports | `reports.operational`, `reports.sla`, `reports.cost`, `reports.assets`, `reports.export` |
| Integrations / admin | `integrations.manage`, `apikeys.manage`, `webhooks.manage`, `retention.manage`, `features.manage` |

> **Naming rule (implemented in slice 1):** the database enforces `^[a-z_]+\.[a-z_]+$` on permission keys, so keys have exactly two parts and use underscores for qualifiers (`tickets.read_own`, not `tickets.read.own`). The table above is shown with dotted qualifiers for readability and is mapped to this form when each module is built. Slice 1 added `locations.read|manage`, `teams.read|manage` and `calendars.read|manage`.

Every one of these appears in the RBAC matrix (doc 09, to be extended) with the personas that hold it by default.

## 9. Domain model (high level)

Core entities (all with `tenant_id`, `created_at/by`, `updated_at/by`, optimistic-concurrency `row_version`; soft-delete where history matters):

`Site/Location`, `Department`, `CostCenter`, `Team`, `TeamMember(skills, shift)`, `BusinessCalendar`, `Holiday`, `ServiceCategory`, `Service`, `FormDefinition(version)`, `Ticket`, `TicketFormAnswer`, `TicketComment`, `TicketAttachment`, `TicketEvent (timeline)`, `TicketLink`, `WorkOrder`, `ChecklistTemplate/Item/Result`, `SlaPolicy`, `SlaTarget`, `SlaClock (periods)`, `AssignmentRule`, `ApprovalPolicy/Step`, `ApprovalRequest/Decision`, `Delegation`, `Asset`, `AssetType`, `PmSchedule`, `InventoryItem`, `StockLocation`, `StockMovement (ledger)`, `SupplyRequest/Line`, `BookableResource`, `Booking`, `VehicleTrip`, `Visitor/VisitorPass`, `MailItem`, `Vendor/Contract`, `CostLine`, `Budget`, `KbArticle(version)`, `NotificationTemplate/Preference/Delivery`, `ApiKey`, `Webhook/Delivery`, `Job`, `RetentionPolicy`.

Design rules:
- Ticket number from a per-tenant counter inside the creating transaction (no gaps required, no duplicates).
- Booking overlap prevented with a PostgreSQL `EXCLUDE USING gist (tenant_id WITH =, resource_id WITH =, tstzrange(start,end) WITH &&)` constraint.
- Stock quantity is a **derived sum of the movement ledger** (cached balance updated in the same transaction).
- SLA clock stored as periods (`started_at, paused_at, resumed_at, reason`), so elapsed working time can be recomputed and audited.
- Timestamps `timestamptz` (UTC); local time derived through the site/tenant time zone.
- Free-text and form answers are treated as untrusted (output-encoded, size limited).

## 10. Non-functional requirements

| Area | Requirement |
|---|---|
| **Security** | OWASP ASVS L2 target. All base controls kept (Argon2id, hashed tokens, CSRF, `__Host-` cookies, rate limits, MFA, SSO). Additional: file upload hardening and malware-scan hook, signed URLs, SSRF-safe webhook delivery (block private ranges, DNS re-resolution), secrets encrypted with versioned keys, per-tenant API-key hashing, dependency and container scanning in CI, annual pen-test before GA |
| **Privacy** | UU PDP (Indonesia) / GDPR-aligned: data inventory, purpose limitation, consent text for visitors, retention and anonymisation, data-subject export and deletion (reuse account export/closure), DPA template, data residency choice of region |
| **Performance** | p95 API read < 300 ms, write < 500 ms at 500 concurrent users / tenant and 100 tenants; ticket list (50 rows) < 1 s with 1 M tickets per tenant; search < 1 s; dashboards < 2 s using rollups; load test in CI-nightly with budgets |
| **Scalability** | Stateless API horizontally scalable; Postgres with proper composite indexes `(tenant_id, …)`; background workers scaled independently; attachments in object storage |
| **Availability** | 99.9 % monthly for the SaaS; zero-downtime deploys with backward-compatible migrations (expand/contract); health, readiness and dependency checks |
| **Recovery** | RPO ≤ 15 min (WAL archiving / PITR), RTO ≤ 4 h; backups encrypted, restore **drill every quarter** and evidenced; object-storage versioning |
| **Observability** | Structured logs with correlation id and tenant id (no PII/secrets), metrics (RED + queue depth + SLA-timer lag), distributed traces, alerting runbooks, audit of admin access; status page |
| **Compatibility** | Latest two versions of Chrome, Edge, Safari, Firefox; iOS 16+/Android 10+ PWA |
| **Maintainability** | Clean layering retained (Domain → Application → Infrastructure → Api); state machines and SLA maths are pure, unit-tested domain code; API versioned `/v1`; OpenAPI generated and contract-tested |
| **Compliance evidence** | Audit export, access-review reports (existing), change log, SOC 2-style control mapping (P2) |

## 11. Technical approach (fit with this repository)

- **Keep the stack**: Next.js 15 UI (`src/`), ASP.NET Core / .NET 10 API (`api/`), PostgreSQL 16, EF Core migrations. The GA modules are added as new feature areas in each layer, reusing: tenant resolution, session/CSRF/proxy model, `AuthorizationService`, `AuditWriter`, email outbox, settings, i18n, design system, Playwright and xUnit/Testcontainers harnesses.
- **New backend pieces**: job runner (hosted service + `jobs` table with `FOR UPDATE SKIP LOCKED` leasing), storage port (local-disk dev / S3-compatible prod), notification port with channel adapters, SLA engine and state machines in `Domain`, search (PostgreSQL full-text + trigram in v1; external engine only if measured need), PostgreSQL RLS setup, scoped-permission evaluation.
- **New frontend pieces**: requester portal, agent workspace (queue + board + detail), mobile PWA views, form renderer/builder, calendar/booking UI, reports, configuration screens. Server remains the only authority; the UI only reflects effective permissions.
- **Decision records** to write before coding the affected area: ADR-GA-01 scoped RBAC, ADR-GA-02 SLA engine and clock model, ADR-GA-03 job system, ADR-GA-04 file storage/AV, ADR-GA-05 RLS strategy, ADR-GA-06 reporting (rollups vs replica), ADR-GA-07 offline PWA.
- **Documentation to extend**: `07-data-model`, `08-api-spec`, `09-rbac-matrix`, `10-security-and-threat-model` (new threats: attachment abuse, SSRF via webhooks, vendor over-reach, approval-link replay, cross-tenant leakage, SLA tampering), `11-test-plan`.

## 12. Release plan

| Release | Contents | Exit criteria |
|---|---|---|
| **R1 — MVP (P0)** | Tenant onboarding, locations, teams, catalog + forms, ticket lifecycle, assignment rules, SLA + calendars + escalation, approvals (e-mail link), comments/attachments, notifications (in-app/e-mail), operations + SLA dashboards, tenant admin config, GA templates, audit, i18n EN/ID, PWA basics | Section 14 checklist fully green for R1 scope; pilot tenant live ≥ 4 weeks with no Sev-1 |
| **R2 — Operations (P1 part 1)** | Work orders + checklists, assets + QR, PM, vendors + vendor portal, cost/budget, CSAT, KB, bulk/saved views, e-mail-to-ticket, API + webhooks | Pen-test findings High/Critical closed; load test budgets met |
| **R3 — Facilities suite (P1 part 2)** | Bookings (rooms/vehicles/parking), visitors, mail/courier, inventory/supplies, scheduled reports, SSO group mapping + directory sync, WhatsApp/Teams/Slack, offline queue | Utilisation/cost reports reconcile with source data in UAT |
| **R4 — Scale (P2)** | Meter-based PM, config promotion, chat-ops, calendar sync, SOC 2 mapping, multi-currency | — |

## 13. Success metrics

Adoption (active requesters / headcount, % requests in-app), efficiency (median first-response and resolution time, backlog age), quality (SLA attainment ≥ 95 %, CSAT ≥ 4.2/5, reopen rate < 5 %), cost (cost per ticket, PM vs reactive ratio, supply stock-outs), reliability (availability, job lag, error rate), onboarding (time to configure a new tenant < 1 day).

## 14. Production-readiness checklist (release gate)

Nothing ships to production until every item is evidenced (link or test name) in the release report.

**Security & privacy**
- [ ] Threat model updated and reviewed; all new endpoints in the authorization-coverage test (deny by default)
- [ ] Cross-tenant isolation tests for every endpoint/query; RLS enabled and tested
- [ ] Scoped-permission tests (site/team/own, vendor, requester) including DTO leakage checks
- [ ] Upload, webhook (SSRF), approval-link, API-key and rate-limit abuse tests
- [ ] Secrets only via environment/secret store; startup checks refuse insecure production config (existing `StartupChecks` extended)
- [ ] Dependency, container and static analysis clean of High/Critical; external pen-test remediated
- [ ] Privacy review, retention policies active, data export/deletion verified

**Quality**
- [ ] Unit tests for state machines, SLA/calendar maths (time zones, DST, holidays, pauses), assignment, approvals, stock ledger, booking conflicts
- [ ] Integration tests on real PostgreSQL (Testcontainers) incl. concurrency (double booking, double approve, ticket-number race, stock oversell)
- [ ] E2E (Playwright) for the golden paths of each persona, with axe accessibility checks
- [ ] Load test meets §10 budgets; migration test on a production-sized dataset

**Operations**
- [ ] CI/CD with gated pipeline; backward-compatible migrations run as a release step, tested rollback
- [ ] Backups + PITR configured; **restore drill performed and timed**
- [ ] Monitoring, alerts and on-call runbooks (job lag, SLA-timer lag, queue depth, error rate, disk, DB)
- [ ] Health/readiness endpoints, graceful shutdown of workers, zero-downtime deploy rehearsal
- [ ] Capacity plan, rate limits and plan quotas enforced
- [ ] Support process: tenant support mode with consent, incident and communication templates, status page

**Product**
- [ ] UAT signed off by pilot tenant; onboarding wizard produces a working helpdesk without engineer help
- [ ] Admin, agent and requester documentation + in-app help; seed templates reviewed by a real GA team
- [ ] Bahasa Indonesia copy reviewed by a native speaker

## 15. Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Scope too large for one release | Delay, low quality | Strict R1 = P0 only; module toggles; vertical slices as in earlier phases |
| SLA/calendar logic errors | Wrong breach data, loss of trust | Pure domain engine, property-based tests, stored clock periods, recompute tool |
| Cross-tenant data leak | Critical breach | Three-layer isolation, RLS, automated tenant-fuzz tests, pen-test |
| Configurability turns into complexity | Slow onboarding, bugs | Opinionated templates, wizard, limits on rule/form depth, config validation |
| Offline/mobile sync conflicts | Lost technician updates | Last-write rules per field, server-side state-machine validation, conflict UI; ship after R2 |
| Notification channel reliability (WhatsApp) | Missed alerts | Outbox + retry + fallback channel, delivery tracking |
| Report load affects OLTP | Slow app | Rollup tables/materialised views, replica option, query budgets |
| Vendor/third-party access over-reach | Data exposure | Scoped vendor role, separate DTOs, access reviews |

## 16. Assumptions and open questions

Assumptions used above (please confirm or correct):

1. **Multi-tenant SaaS from day one** (the repository already has tenants and support mode), but a single-company install must remain possible.
2. Deployment stays on the existing Docker/Coolify path (`deploy/`); object storage is S3-compatible (MinIO or cloud).
3. Currency default IDR, time zone default Asia/Jakarta, languages EN + ID.
4. Approvers and managers come from the user directory (manual/CSV first, HRIS/SCIM in R3).
5. WhatsApp is delivered through a tenant-supplied provider (Meta Cloud API or an Indonesian BSP); we do not resell messaging.

Open questions for the PO:

| # | Question | Why it matters |
|---|---|---|
| Q1 | Who are the first target customers/pilot and their size (users, sites, tickets/month)? | Sizing, load targets, template choices |
| Q2 | Is the product sold as SaaS (pricing tiers/plan limits) or installed per company? | Quotas, billing hooks, operator console |
| Q3 | Which GA domains are mandatory for the pilot (vehicles? visitors? inventory?) | Could pull R3 modules earlier |
| Q4 | Required integrations for the pilot (HRIS, SSO provider, WhatsApp provider, accounting/PO) | INT scope and timeline |
| Q5 | Data-residency or on-premise requirements (Indonesian PDP, government/BUMN customers)? | Hosting, key management |
| Q6 | Are vendors to be given logins, or only e-mail-based interaction in R2? | Vendor portal scope |
| Q7 | Compliance targets (ISO 27001, SOC 2) required at launch? | Control evidence workload |
| Q8 | Brand/design: reuse the Annaba/Azure Executive design system as-is, or per-tenant theming only? | UI effort |
| Q9 | Should the template-init script be run first (renaming to the final product name) before development starts? | Avoid rename churn in new modules |

## 17. Glossary

**GA** General Affairs · **ATK** *Alat Tulis Kantor* (office stationery) · **SLA** service-level agreement · **OLA** operational-level agreement · **PM** preventive maintenance · **WO** work order · **CSAT** customer satisfaction · **RLS** row-level security · **PITR** point-in-time recovery · **RPO/RTO** recovery point/time objective · **STNK** vehicle registration document · **UU PDP** Indonesian Personal Data Protection Law.
