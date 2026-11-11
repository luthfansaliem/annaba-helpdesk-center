# 55 — GA Helpdesk, release R3 (Facilities suite): plan

R3 in the PRD (`23-ga-helpdesk-prd.md` §12) covers:
- bookings of rooms, vehicles and parking;
- visitors;
- mail and courier;
- inventory and supplies;
- scheduled reports;
- SSO group mapping and directory sync;
- WhatsApp, Teams and Slack;
- the offline queue.

Exit criterion: utilisation and cost reports reconcile with the source data in UAT.

The work is cut into vertical slices, as in R1 and R2. Each slice ships schema, API, screens with their KPI strip (the doc 53 pattern), tests (xUnit, plus Playwright with the accessibility check) and a report. The order follows dependencies and value for a General Affairs team: rooms and vehicles are asked for every day, so bookings come first.

| # | Slice | Requirements | Depends on |
|---|---|---|---|
| R3-1 | **Bookable resources and room booking.** Resources (room, vehicle, parking, equipment) with place, capacity, features, opening hours, buffer, "needs approval" and who may book. Booking with overlap prevented by the database (`EXCLUDE USING gist` on resource and time range, per organisation). Day and week views, and search by time, capacity and feature. *My bookings*. | BKG-001, BKG-002, BKG-003 (search, views) | — |
| R3-2 | **Booking lifecycle.** Approval for resources that need it. Check-in, with no-show rooms released automatically after a grace period. Reminders. Cancellation rules. Recurring bookings with exceptions. A personal calendar feed (ICS, secret link). | BKG-003 (recurrence), BKG-005 (ICS; Microsoft 365 / Google sync stays P2) | R3-1 |
| R3-3 | **Vehicles.** A vehicle booking is a trip: driver (self or assigned), purpose, destination, passengers. At return: odometer out and in, fuel and toll costs (into the cost report), damage notes and a return checklist. Vehicle documents (STNK, insurance, KIR, driver licence) with expiry reminders. Utilisation report for rooms and vehicles (booked vs available hours, no-shows). | BKG-004, RPT-005 (rooms, vehicles) | R3-2 |
| R3-4 | **Visitors.** An employee pre-registers a visitor: name, company, host, purpose, time window, vehicle plate; host and reception are told. Reception checks the visitor in and out, with badge number, an acknowledgement text set by the organisation, and a block list. Visitor volume report. A Receptionist role. Visitor data is anonymised after a configurable period. | VIS-001, VIS-002, VIS-005, RPT-005 (visitors) | — |
| R3-5 | **Mail and courier.** Incoming and outgoing log: carrier, waybill, recipient, status (received → notified → collected), proof photo or name of who collected it; the recipient is told and reminded. Same retention as visitors. | VIS-004, VIS-005 | R3-4 (reception screens) |
| R3-6 | **Inventory.** Items (ATK, pantry, cleaning, uniforms) with unit, SKU, category and reorder level, kept per site. Every change is a ledger movement: receipt, issue, adjustment, transfer. The balance is the ledger sum, cached in the same transaction. Low-stock alerts, a stock-take with variance report, inventory turnover. | INV-001, INV-003, INV-004, RPT-005 (inventory) | — |
| R3-7 | **Supply requests.** A cart of items becomes a request, with approval and a budget check. Fulfilment issues stock from the ledger, including partial fulfilment and back-order. Parts on a work order can be drawn from stock at their cost. Suggested purchase list. | INV-002, INV-005, workflow §7.5 | R3-6 |
| R3-8 | **Scheduled reports and exports.** Any report (operations, SLA, satisfaction, cost, compliance, utilisation, inventory) can be e-mailed as CSV or XLSX on a schedule (daily, weekly, monthly), to people who may see it. Exports are permission-checked, row-limited and audited. | RPT-007 | R3-3, R3-6 |
| R3-9 | **Directory: SSO groups and sync.** Map SSO (OIDC) groups to roles, applied at sign-in. A SCIM 2.0 endpoint (Users, Groups) for identity providers that push. A scheduled CSV import for the rest: departments, managers, sites, employee IDs. Deactivated people are suspended. | INT-003, ADM-004 (sync) | — |
| R3-10 | **WhatsApp, Teams and Slack.** Channel adapters behind the notification port. Credentials are supplied by the organisation and encrypted. People opt in per channel (WhatsApp needs a verified phone number). Deliveries are tracked and retried, and a failed channel falls back to e-mail. | NTF-003 | — |
| R3-11 | **Offline queue for technicians.** First an ADR (ADR-GA-07) and a review of the service-worker rule in doc 06 §10, which today forbids caching API responses. Then: *My work orders* readable offline, and status changes, checklist ticks, time and photos queued and synced, with conflicts shown. | §6.5 offline, ADR-GA-07 | — |

## Permissions
New permissions follow the PRD catalog in the database's two-part form:
- bookings: `bookings.read_all`, `bookings.manage`, `resources.manage`;
- visitors and mail: `visitors.checkin`, `visitors.read_all`, `mail.manage`;
- inventory and supplies: `inventory.read`, `inventory.manage`, `inventory.adjust`, `supplies.fulfil`;
- scheduled reports: `reports.schedule`;
- directory: `directory.manage`.

**Self-service needs no permission** (decided in R3-1, doc 56): booking for yourself, pre-registering your own visitor and asking for supplies are open to everyone signed in, like making a request. The PRD's `bookings.create`, `visitors.preregister` and `supplies.request` are therefore not permissions. The base *User* role must hold nothing: the role hierarchy, delegated invitations and access reviews treat any role with a permission as privileged. Vendor accounts stay out through the vendor portal gate.

As in R1 and R2, organisations that already exist do not get new permissions on their tuned system roles automatically; each slice report says what to add. New roles are seeded for new organisations: *Receptionist*, and *Driver* for vehicles.

## Decisions taken up front
- **Overlaps are the database's job.** The application checks for a friendly message, but the `EXCLUDE` constraint is what holds when two people book the same slot at the same moment. A violation is answered `409` with the clashing booking. The `btree_gist` extension ships with the PostgreSQL 16 image used in every environment.
- **Bookings use the place's time zone.** A booking is stored in UTC; opening hours, "today" and the calendar views use the site's time zone (or the organisation's).
- **Stock is a ledger.** No screen or endpoint edits a quantity directly; corrections are adjustment movements with a reason.
- **External channels never carry secrets or internal notes.** WhatsApp, Teams and Slack get the same text as the e-mail notification and a link, never ticket internals.
- **Test doubles for providers.** WhatsApp (Meta Cloud API), Teams and Slack are tested against local fake receivers, as webhooks are today. Live provider accounts are configured per organisation in production and checked in UAT.
- **Out of R3** (P2 in the PRD): room set-up and catering add-ons (BKG-006), visitor QR passes and gate scanning (VIS-003), Microsoft 365 and Google calendar sync (the ICS feed covers reading), chat-ops ticket creation (INT-004).
