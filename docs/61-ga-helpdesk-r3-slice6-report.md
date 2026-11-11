# 61 — GA Helpdesk R3 slice 6: inventory

Status: **implemented, verification below**. Sixth slice of R3 (`55-ga-helpdesk-r3-plan.md`). It covers:
- INV-001: items;
- INV-003: the stock ledger;
- INV-004: low stock, the suggested purchase list and stock-takes;
- RPT-005 for inventory.

Supply requests from employees come in R3-7.

## What was done
1. **Stock items** (*Assets & vendors → Inventory*).
   - Each item has a code (SKU, unique), name, category (office supplies, pantry, cleaning, uniforms, toiletries, IT, other), unit, the level to reorder at, the usual order quantity and the cost per unit.
   - The list shows how much is where and flags items that are low.
   - It filters by name or code, category, place and "only low".
2. **Stock by place, changed only through the ledger.**
   - *Move stock* records one of four things:
     - **received a delivery**, with the supplier or invoice and the cost, which becomes the item's cost;
     - **handed out**, with who it was for;
     - **a correction**, with a sign and a reason;
     - **moved to another place**, written as two ledger lines that share an id.
   - **The item page** shows the stock per place and every movement with the balance after it.
   - **Nothing edits a quantity directly.** The balance is a cache the database keeps at zero or above: a check constraint, plus an atomic update that only applies when enough stock is left. When two hand-outs race for the last items, one gets "Only 4 pcs … left at this place".
3. **Running low.**
   - When stock at a place falls to the reorder level, the people who keep stock are told once (`stock.low`). The alert re-arms once the stock rises above the level.
   - *To reorder* lists every item and place at or below the level, with a suggested quantity (the usual order quantity, else enough to reach twice the level) and its estimated cost.
4. **KPIs and charts:** items, running low, stock value (at the last purchase cost), units handed out and received in 30 days, units handed out per day, and the items used most.
5. **Stock-takes** (*Inventory → Stock-takes*).
   - Start a count for a place; what is expected is frozen at that moment.
   - Type what is on the shelf.
   - Close the count: every counted difference is posted as a *stock-take* movement. Items not counted stay as they are.
   - One open count per place.
6. **Permissions:**
   - `inventory.read`: GA Agent and up.
   - `inventory.adjust` (corrections and stock-takes): GA Supervisor and up.
   - `inventory.manage` (items, receipts, issues, transfers): GA Manager.
7. **Audit:** item create, update, archive and restore; `inventory.receipt`, `inventory.issue`, `inventory.adjustment`, `inventory.transfer`; `inventory.count_start`, `inventory.count_close`.

## Decisions
- **A ledger, not a quantity field** (PRD §9). Every number on screen can be traced to movements, and the balance always equals their sum.
- **Stock lives at any place, not only sites.** A pantry on level 3 and the store room in the basement are separate places with separate stock.
- **The count posts counted minus expected.** Counting should happen while nothing moves at that place. If stock was handed out during the count, closing can fail with "only N left" rather than go negative.

## Tests added
- **API, `InventoryTests` (2, isolated organisations):**
  - **The ledger:**
    - Requesters get 403 and agents cannot add items; validation covers SKU, name, category and level, and the code is unique.
    - Receipts need `inventory.manage` and set the item's cost.
    - Issuing more than is there gets 409 with the amount left, and nothing can be issued at a place with no stock.
    - An adjustment needs a reason and `inventory.adjust`; a supervisor cannot issue.
    - A transfer writes two lines and the stock is right at both places.
  - **The race:** three hand-outs of 4 compete for the branch's 8. Exactly two succeed and one gets 409, the branch balance is 0, and the ledger sums to the balances.
  - **The low-stock alert:** sent once at the level, not again while low, and again after a recovery and a new dip.
  - **Reorder list and summary:** quantity, suggestion and cost in the reorder list; low lines, stock value, units handed out and received in the summary.
  - **Archiving:** an item with stock cannot be archived; the history has all ten movements.
  - **Stock-take:**
    - Agents cannot start one, only one can be open per place, and an empty one cannot close.
    - Counts give the counted and difference totals; negative counts are refused.
    - Closing moves coffee from 12 to 10 with a count movement of −2, leaves the uncounted sugar unchanged, and refuses a second close.
- **Playwright, `inventory.spec.ts`:**
  - GA adds an item, receives 20 and hands out 15 at a new store.
  - The item shows 5 pcs and "Low", and the reorder list says "Order 100 pcs".
  - A stock-take counting 4 posts the difference, so the API reports 4.
  - The new-item form, the item page, the inventory page and the stock-take pass the accessibility check.

## Known gaps
- **No purchase orders.** The reorder list is a list; buying happens outside the system and comes back as a receipt.
- **No batch or expiry tracking** (for pantry items).
- **No barcode scanning** of items yet; the asset QR flow (R2) can be extended to stock items later.

## Verification
| Check | Result |
|---|---|
| `tsc --noEmit`, `eslint` | pass |
| `next build` | pass |
| `dotnet test api/AnnabaDigital.sln` | **661 passed, 0 failed** (run together for R3-3 to R3-6) |
| Full Playwright suite | 230 passed, 1 failed, run together for R3-3 to R3-6. The failure was the new `inventory.spec.ts`: its label selectors were ambiguous (`Unit` also matched *Cost per unit*). The test now uses the field ids. It passes on its own, and the other 232 tests passed with R3-7 and R3-8 (doc 62) |
