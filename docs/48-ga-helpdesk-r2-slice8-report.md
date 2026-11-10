# 48 — GA Helpdesk, R2 slice 8: cost and budget

Status: **implemented and verified** (API and Playwright suites on the final code; see the table at the end). Eighth slice of release R2 (`40-ga-helpdesk-r2-plan.md`): **CST-001** (cost lines including labour as rate × time and vendor invoices; currency per organisation), **CST-002** (cost centres; chargeback by department, site and service; monthly report export), **CST-003** (budgets per cost centre, site or category with consumption and threshold alerts), **CST-004** (money as decimals, rounding documented) and **RPT-003** (cost report by department, site, category, vendor and month; budget vs actual).

## What was done
1. **Currency and labour rate** (*Budgets* page, for budget managers): the organisation's currency code (IDR by default; a label — nothing is converted) and an optional hourly labour rate. With a rate, time logged on work orders counts as cost (minutes × rate ÷ 60), and the work order page shows *labour from logged time* next to the cost total.
2. **Vendor invoices**: a new cost kind on work orders, next to part, labour, service and other.
3. **Cost centres** (*Helpdesk → Cost Centres*): a code, a name and the places they pay for. A place belongs to one cost centre; a cost at a place is charged to the nearest place up the tree that has one.
4. **Cost report** (*Helpdesk → Cost Report*, `cost.read`): for a period (default the last 12 months, up to two years), totals by month, site, cost centre, service category, service, the requester's department, vendor or asset — split into parts, labour (lines plus logged time), services, vendor invoices and other, with the number of work orders — and a **CSV export** (UTF-8; text that looks like a spreadsheet formula is defused).
5. **Budgets** (*Helpdesk → Budgets*): a year's amount for a site, a cost centre or a service category, with what is spent that year, the percentage and a bar (*On track*, *Near the limit* from 80 %, *Used up* from 100 %). Budget managers are alerted (in the app and by e-mail, `budget.threshold`) once at 80 % and once at 100 %; changing the amount or target starts the alerts over.

## Rules worth knowing
| Rule | Why |
|---|---|
| All money is `numeric` with two decimals; computed amounts (quantity × unit cost, minutes × rate ÷ 60) are rounded **half away from zero per line**, then added. Work-order totals now use the same rounding (before: banker's rounding). | CST-004: no floating point, one documented rule everywhere. |
| A cost line is dated when it was recorded; logged time by its work date. | Matches what people see on the work order. |
| Attribution goes through the work order's ticket (place, service, requester) and the work order (vendor, asset). | Every cost belongs to a request; no separate bookkeeping. |
| `cost.read` sees the whole organisation's costs; no team scoping. | It is a finance view; give it to supervisors and managers only (the seeded roles do). |
| Budgets of a site use top-level places only; one budget per target and year. | Clear, non-overlapping totals. |
| Alerts are claimed with a conditional update (once per level, even with several instances). | Same pattern as the other reminders. |

## Tests added
**API — `CostTests` (2, each in an isolated organisation):** settings validation and permission; cost-centre codes stored upper case and one cost centre per place; rounding (1.5 × 2.03 = 3.05), labour from 90 minutes at 120,000/h = 180,000, a vendor invoice; the work order shows the labour cost to supervisors and not to agents; the report by site (each kind, total, work orders, grand total across sites), by cost centre (including "no cost centre"), vendor, category, department and month; the CSV header with the currency and a defused `=Finance`; agents refused, unknown grouping and reversed dates refused. Budgets: only managers create, validation (a floor is not a site, year, amount precision), one per target and year; spent and percentage; no alert below 80 %; the 85 % alert once (in-app and by e-mail), the "used up" alert at 100 %, a raised amount re-alerts at the new percentage; delete.
**Playwright — `costs.spec.ts` (1) and `/costs`, `/costs/budgets`, `/costs/centers` in the accessibility sweep (light and dark):** a cost centre is created for a new site; a 1,000 budget for the site shows *On track (0%)*; a 900 vendor invoice on a work order there turns it *Near the limit (90%)*; the cost report by cost centre shows 900.00 for it and the export link carries the grouping; the CSV contains the cost centre's row; requesters get `403`; the pages pass the accessibility check.

## Decisions and known gaps
- **One currency per organisation**, no conversion (multi-currency with stored rates is P2 in the PRD).
- **No chargeback documents or invoices** are produced; the export is the hand-off to finance.
- **Approvals do not check budget availability yet** (CST-003's last clause): requests carry no cost estimate at approval time; this needs a cost field on requests and is left for the purchase/supply work (R3, INV-002).
- **Per-person labour rates** are not offered; one organisation rate.
- The report reads at most 200,000 cost lines and 200,000 time entries per request — far beyond a year of a large site, but not unlimited.
- **Existing organisations** must add the permissions to their GA roles by hand (runbook).

## Verification of the functional behaviour
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16, Testcontainers) | **632 passed, 0 failed** (630 before; +2 `CostTests`) |
| `tsc --noEmit`, `eslint`, `next build` | pass (one pre-existing `<img>` warning in the app icon) |
| Full Playwright suite (light + dark accessibility, responsive) | **216 passed, 0 failed** (209 before; +1 cost and budget, +6 accessibility pages) |

## Next
R2-9: e-mail to ticket — an inbound mailbox where new e-mails become requests and replies thread onto their tickets.
