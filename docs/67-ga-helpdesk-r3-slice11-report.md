# 67 — GA Helpdesk R3 slice 11: field mode (offline work orders)

Status: **implemented, verification below**. Eleventh and last slice of R3 (`55-ga-helpdesk-r3-plan.md`). It covers PRD §6.5 (offline), as decided in **ADR-GA-07** (doc 66).

## What was done
1. **Field mode** (*Service desk → Field mode*, `/work-orders/field`, `workorders.complete`).
   - One page that keeps working without a connection once it is open.
   - It shows the person's own open work orders and, for the one chosen:
     - the instructions, checklist, status and time logged;
     - start, hold, resume and complete (hold and complete ask what is happening);
     - checklist ticks, a time log and an *after* photo from the camera.
2. **The snapshot.**
   - `GET /api/work-orders/offline` returns at most 50 of the caller's own open work orders, with checklists, without costs.
   - It is stored in IndexedDB under the person's user id. It is used for at most 72 hours, and it is wiped:
     - on sign-out;
     - when another person's snapshot would replace it;
     - with *Forget this device's data*.
3. **Changes offline.**
   - Each change shows at once and is queued in IndexedDB (photos as blobs). The queue survives a closed tab.
   - The status line says *Online* or *Offline* and how many changes are waiting.
4. **Syncing.** On the `online` event, on opening the page and with *Sync now*, the queue is replayed in order through the normal API.
   - **Idempotency:** each change carries an `Idempotency-Key`. Work-order changes remember a key per person for 7 days, only once the change succeeded. A repeated key answers with the work order as it is (`Idempotent-Replayed: true`) without doing the change again.
   - **Version check:** status changes carry the version they were made on, so a work order changed meanwhile answers 409.
   - **Refusals:** a refused change becomes a conflict, shown with the server's reason, *Try again* and *Discard*. Later changes to the same work order wait behind it, and nothing is merged silently.
   - **Network or session problems** keep the change waiting.
5. **The service worker is unchanged:** it still never caches HTML, RSC or API responses, and its tests still pass. The static offline page now points technicians to field mode.

## Decisions
See ADR-GA-07 (doc 66):
- data in the app's IndexedDB, not in the service worker;
- only the person's own open work orders;
- expiry and wipe rules;
- idempotency keys;
- conflicts shown, never merged.

## Tests added
- **API, `FieldModeTests` (1, an isolated organisation):**
  - The snapshot has only the agent's own open work order: not a colleague's, not a cancelled one. It includes the checklist, and costs are null. Requesters get 403.
  - Starting with a key works. A time log sent twice with the same key logs 30 minutes once, and the second answer says `Idempotent-Replayed` with the work order as it is.
  - A hold with an old version gets 409 and is not remembered, so the same key succeeds with the current version.
  - Another agent cannot use someone's work order with a replayed key (403, not a replay answer).
  - A malformed key gets 422.
- **Playwright, `field.spec.ts`:**
  - **Online:** the admin opens field mode with a work order assigned to them, starts it, and nothing is waiting.
  - **Offline** (the browser context taken offline): ticking the checklist and logging 25 minutes shows "2 changes waiting" and "25 min logged" at once.
  - **Back online:** the queue drains, and the API reports 25 minutes and the ticked item.
  - *Forget this device's data* removes it.
  - The page passes the accessibility check.

## Known gaps
- **Reloading while offline** shows the static offline page, by design: HTML is never cached.
- **Data on the device is not encrypted at rest** (ADR-GA-07, consequences).
- **No offline creation** of work orders or tickets, and no offline costs.

## Found and fixed during verification
- The Playwright test first asked `/api/me` for the technician's id; the right route is `/api/auth/me`.
- Checklist ticks in field mode change once the change is saved on the device (asynchronously). The test therefore clicks and then waits for the tick, instead of using the synchronous `check()`.

## Verification
| Check | Result |
|---|---|
| `tsc --noEmit`, `eslint` | pass |
| `next build` | pass |
| `dotnet test api/AnnabaDigital.sln` | **670 passed, 0 failed** (clean full run of all of R3) |
| Full Playwright suite | **236 passed, 0 failed** (clean full run of all of R3) |
