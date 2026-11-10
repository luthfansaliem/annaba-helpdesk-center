# 44 — GA Helpdesk, R2 slice 4: vendors and contracts

Status: **implemented and verified** (API and Playwright suites on the final code; see the table at the end). Fourth slice of release R2 (`40-ga-helpdesk-r2-plan.md`): **VND-001** (vendor directory with contacts and the places a vendor covers), **VND-004** (contracts with dates and value, expiry reminders, documents), **VND-005** (blocking a vendor) and the first part of **VND-003** (a scorecard from the vendor's work orders). Work orders can now be given to a vendor with a purchase order number.

## What was done
1. **Vendor directory** (*Helpdesk → Vendors*): search by name or what they do, filter by status (active, blocked, all) and by place — a vendor covering a building also shows up for the floors and rooms below it. Each row shows the contact phone/e-mail, open work orders and the next contract end.
2. **A vendor's page**: details (phone, e-mail, website, address, tax number, notes), **contacts** (up to 20, with role, phone and e-mail), **places they cover**, **contracts** (title, number, start, optional end, value, notes; shown as *Active*, *Not started* or *Ended*), **documents** (contract scans, certificates; same file checks as ticket files, up to 50), and **how their work goes**: work orders given, done, open, done on time vs. late, and cost (only for people who may see costs).
3. **Blocking**: a manager blocks a vendor with a reason (shown on its page). A blocked vendor can't be chosen for new work orders; work it already has is left as it is, and a work order that already has the vendor can still be edited. Unblocking is one click.
4. **Work orders given to a vendor**: the work order form has a *Vendor* (active vendors, or *None — done in-house*) and a *Purchase order* number. The work order page shows the vendor, its phone and the PO; the ticket's work-order panel shows the vendor's name. The vendor's page lists its latest work orders.
5. **Contract reminders**: with the daily reminders, people who manage vendors get a notification (in the app and by e-mail) 30 days before a contract ends and again when it ends; more than five at once become one digest. Changing a contract's end date starts the reminders over. Contracts that ended long ago stay silent. The kind `vendor.contract` can be switched off per person under *Notifications → Settings → Vendors*.
6. Vendor documents are part of the **file check** (`/api/retention/files`) and of the files backup, like every other stored file.

## Rules worth knowing
| Rule | Why |
|---|---|
| `vendors.read` for GA Agent, Supervisor and Manager; `vendors.manage` only for GA Manager. | Agents need the phone number; only managers change suppliers, contracts and blocks. |
| Choosing a vendor on a work order is part of `workorders.manage`; the pick list only offers active vendors. | Keeps the work order form one permission; blocked vendors can't slip in. |
| The scorecard's cost is `null` without `workorders.costs`. | Same rule as work orders: agents never see money. |
| Contacts and places are replaced as a whole when a vendor is saved, with a version check. | Simple form, no lost updates between two managers. |
| A contract can't end before it starts; amounts have two decimals and no currency. | Same money rule as work-order costs. |
| Reminder levels are claimed with a conditional update. | Two API instances never send the same reminder twice. |

## Tests added
**API — `VendorTests` (4):** directory and permissions (a requester is refused; agents and supervisors read but can't change; search and place filters including a place below a covered one; validation of name, e-mail, website and contacts; contacts and places replaced on save; stale version refused); contracts (state, end before start and negative value refused, deleting) and documents (linked to a contract, download with sandboxing headers, a disallowed type and an infected file refused, delete); work orders given to a vendor (the PO and vendor phone on the work order, the vendor on the ticket's panel, PO length, the scorecard with done/open/on-time and cost for supervisor and manager but `null` for an agent, open work orders in the list, block reason required, a blocked vendor refused for new work but kept on existing work and missing from the pick list, the blocked filter, unblocking, the pick list refused to agents); contract reminders in an isolated organisation (only the contract ending within 30 days is announced — not the long-ended one nor the open-ended one — in the app and by e-mail, and not twice).
**Playwright — `vendors.spec.ts` (1) and `/vendors` in the accessibility sweep (light and dark):** a vendor is added with a contact and a place, a contract and a document are added, the list finds it by what it does, a work order on a ticket is given to it with a PO (shown on the ticket and the work order page), its page lists the work order and the scorecard, blocking needs a reason and removes it from the pick list, unblocking restores it, and a requester gets `403`; the list and the vendor page pass the accessibility check.

## Decisions and known gaps
- **Vendor portal** (vendors updating their own work orders) is R2-11; for now staff record the vendor's work.
- **Assets are not linked to a vendor** yet (maintenance vendor per asset comes with preventive maintenance, R2-5); **costs per vendor** reporting comes with R2-8.
- **Scorecard** is counts only (no ratings, no response-time SLA per vendor); "on time" needs a due date on the work order.
- **No currency field**: values are in the organisation's currency; multi-currency is R4.
- **Reminder tests** cover the 30-day notice only; the "has ended" notice and the reset after a new end date use the same code path but have no test of their own yet.
- **Existing organisations** must add the permissions to their GA roles by hand (runbook).

## Verification of the functional behaviour
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16, Testcontainers) | **620 passed, 0 failed** (616 before; +4 `VendorTests`) |
| `tsc --noEmit`, `eslint`, `next build` | pass (one pre-existing `<img>` warning in the app icon) |
| Full Playwright suite (light + dark accessibility, responsive) | **198 tests: 197 passed, 1 failed** in the full run (195 before; +1 vendors, +2 accessibility pages). The failure was the new vendors test itself (its `Search` locator also matched the global search button); fixed and re-run on its own: **passed** |

## Next
R2-5: preventive maintenance — schedules per asset or place that create work orders from saved checklists.
