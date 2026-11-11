# 62 — GA Helpdesk R3 slice 7: supply requests

Status: **implemented, verification below**. Seventh slice of R3 (`55-ga-helpdesk-r3-plan.md`). It covers:
- INV-002: supply requests (a cart of stock items), with approval and fulfilment from the ledger;
- INV-005: parts on a work order drawn from stock at their cost.

## What was done
1. **Asking for supplies** (*Supplies*, for everyone signed in).
   - The cart shows every active stock item with what is available at the chosen place (the organisation's default place is preselected).
   - The requester types how many of each item they need, adds a note, and sees the estimated value.
   - Requests get a number per organisation and year (`SR-2026-0042`).
   - Like raising a ticket, asking needs no permission. Vendor accounts cannot reach it (the vendor portal gate).
2. **Approval above an amount.**
   - GA Manager sets *Manager approval from (amount)* under *Supplies → Rules*.
   - A request worth that much or more waits for the requester's manager (from the profile). The manager is told (`supply.approval`) and decides under *Waiting for your approval*.
   - When the requester has no active manager, the supplies desk approves instead.
   - A rejection needs a reason; the requester is told either way (`supply.update`).
3. **Issuing from stock** (the supplies desk: `supplies.fulfil`, GA Supervisor and up).
   - The desk sees all requests. Requests to issue are flagged, and the desk is told about new or approved ones (`supply.new`).
   - *Issue* prefills each line with what is outstanding and in stock. Each quantity becomes an *issue* movement in the ledger, grouped under the request and referencing its number and the requester.
   - **Partial fulfilment and back-order.** What is not issued yet stays on back-order and the request is *partly issued*. When stock is short, the whole issue is refused with "Only N … left", so nothing is half-written.
   - The requester is told when items are ready to collect, whether part or all of the request.
   - *Close* ends a request whose rest will not be issued; it needs a reason.
   - The requester can cancel a request as long as nothing was issued.
4. **Parts from stock on a work order** (INV-005).
   - On a work order, *Part from stock* picks a place and an item that is in stock there.
   - The stock goes down through the ledger (grouped under the work order), and a *part* cost line is added at the item's cost.
   - Removing that cost line puts the part back with a return movement.
   - It needs `workorders.costs`; vendor accounts cannot use it.
5. **KPIs:** waiting for approval, to be issued (and partly issued), issued in 30 days, the value issued in 30 days, and the most requested items.
6. **Audit:** `supply.request`, `supply.approve`, `supply.reject`, `supply.fulfil`, `supply.close`, `supply.cancel`, `supply.settings`, `workorder.part_from_stock`, `workorder.part_returned`.

## Decisions
- **No permission to ask.** The base `user` role must hold no permissions (see doc 56), so asking is open to anyone signed in, like the portal. The desk, approval rules and summary are protected.
- **The manager approves, the desk issues.** Approval follows the reporting line already used for ticket approvals (`profiles.manager_id`). The desk only steps in when there is no manager, so a request is never stuck.
- **All or nothing per issue.** One issue is one transaction. If any line is short, nothing moves; the desk lowers that quantity and the rest stays on back-order. A concurrent issue of the same request gets 409 (version check), so a line can never be issued twice.
- **The estimate uses the item's cost when asked.** Issued value in the summary uses the cost on each movement.

## Tests added
- **API, `SupplyTests` (2, isolated organisations):**
  - **Request to fulfilment:**
    - Rules are kept by GA Manager.
    - The catalogue shows availability at the default place, and an empty cart or a zero quantity is refused.
    - A small request opens at once and the desk is told.
    - A large one waits for the manager, who is told. Others get 404, and the desk gets 403 on deciding and 409 on issuing early.
    - A rejection needs a reason; approval opens the request.
    - Agents cannot issue, and requesters cannot see the desk.
    - Issuing more than outstanding is refused (422) and more than in stock is refused (409, "Only 6 ream").
    - A partial issue leaves 2 reams on back-order, and the requester is told "Part of …".
    - Cancelling after an issue is refused. The ledger shows two issue movements under the request.
    - After a delivery, the back-order is issued and the request is fulfilled.
    - Cancel by the requester works; a close needs a reason.
    - Lists are per requester, and the summary gives the count fulfilled and the value issued.
  - **No manager and work order parts:**
    - Without a manager, the desk approves.
    - A part from stock needs a quantity and enough stock, and costs 2 × the item's cost.
    - Requesters get 403.
    - Removing the line returns the stock (ledger −2 / +2 under the work order).
- **Playwright, `supplies.spec.ts`:**
  - An employee asks for 3 pads from a new store.
  - GA issues them from the desk.
  - Both see *Issued*, and stock is 7.
  - The cart, the issue dialog and both views of the page pass the accessibility check.

## Known gaps
- **No delivery to desks.** Items are collected at the store; the notification says so.
- **No budget check against cost centres yet.** The approval amount is the budget rule for now; budgets (R2) are per category and place, not per person.
- **No purchase order from a back-order.** The reorder list (R3-6) shows what to buy.

## Verification
| Check | Result |
|---|---|
| `tsc --noEmit`, `eslint` | pass |
| `next build` | pass |
| `dotnet test api/AnnabaDigital.sln` | **665 passed, 0 failed** (run together for R3-7 and R3-8) |
| Full Playwright suite | 232 passed, 1 failed. The new `supplies.spec.ts` and `report-exports.spec.ts` passed. The failure was the R3-6 `inventory.spec.ts`, whose label selectors were ambiguous; it was fixed and passes on its own (see doc 61) |
