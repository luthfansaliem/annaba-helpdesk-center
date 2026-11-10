# 40 — GA Helpdesk, release R2 (Operations): plan

R2 in the PRD (`23-ga-helpdesk-prd.md` §12): work orders and checklists, assets and QR, preventive maintenance, vendors and the vendor portal, cost and budget, CSAT, knowledge base, e-mail-to-ticket, API and webhooks (bulk actions and saved views were already delivered in R1 slice 9). Exit criteria: pen-test High/Critical closed, load-test budgets met.

The work is cut into vertical slices like R1: each one ships schema, API, screens, tests (xUnit + Playwright with accessibility) and a report. Order follows dependencies: assets first, because work orders and maintenance hang off them.

| # | Slice | Requirements | Depends on |
|---|---|---|---|
| R2-1 | **Asset register**: types with their own fields, assets (tag, serial, place, owner, status, purchase, warranty, vendor name), lifecycle events, link a ticket to an asset, the asset's ticket history, CSV export and import with a validation report | AST-001, AST-004, AST-006, AST-003 (history part), TKT-001 (asset field) | — |
| R2-2 | **QR labels and reporting from the asset**: printable label sheets, scan → asset page → "report a problem" pre-filled; warranty reminders; failure count and "replace vs repair" hint | AST-002, AST-003, AST-005 | R2-1 |
| R2-3 | **Work orders and checklists**: child work orders on a ticket (own assignee, checklist, labour time, parts and cost lines), required checklist items block completion, photo evidence, ticket resolves when required work orders are done | TKT-006, TKT-007, TKT-014 (manual time) | R2-1 |
| R2-4 | **Vendors**: vendor register and contracts, assign a work order to a vendor, contract expiry reminders | VND (basic) | R2-3 |
| R2-5 | **Preventive maintenance**: schedules by asset, type or place with a checklist; the scheduler creates work orders ahead of time, catches up after downtime, never duplicates; compliance report | PM-001, PM-002, PM-003 | R2-1, R2-3 |
| R2-6 | **Satisfaction (CSAT)**: rating and comment after resolution, low-score alert, report per agent/team/site | TKT-009 | — |
| R2-7 | **Knowledge base**: articles with versions, publish, search, suggestions while making a request | KB | — |
| R2-8 | **Cost and budget**: cost lines roll-up, budgets per cost centre, cost reports | CST | R2-3, R2-4 |
| R2-9 | **E-mail to ticket**: inbound mailbox, replies thread onto tickets | TKT-004 (by e-mail), INT | — |
| R2-10 | **API keys and webhooks**: per-tenant keys (hashed), SSRF-safe delivery | INT | — |
| R2-11 | **Vendor portal**: vendor logins limited to their work orders (PRD open question Q6) | VND | R2-4 |

Permissions follow the PRD catalog in the database's two-part form: `assets.read`, `assets.manage`, `assets.import`; later `workorders.*`, `pm.*`, `vendors.*`, `kb.*`, `budgets.*`. Existing organisations do not get new permissions on their tuned system roles automatically (as in R1): the report of each slice says what to add.
