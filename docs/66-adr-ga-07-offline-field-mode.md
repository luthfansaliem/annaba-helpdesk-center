# 66 — ADR-GA-07: offline field mode for technicians

Status: **accepted** (R3 slice 11). Review of doc 06 §10 (PWA constraint) as the R3 plan requires.

## Context
Technicians work in basements, plant rooms and lifts with no signal. PRD §6.5 asks for two things:
- *My work orders* readable offline;
- status changes, checklist ticks, time and photos recorded offline and synced later, with conflicts shown.

Doc 06 §10 says the service worker caches only hashed assets and one static offline page, and **never HTML, RSC or API responses**. It says any offline feature needs a separate security review. This ADR is that review.

## Options
1. **Let the service worker cache work-order API responses and the page HTML.**
   - The SW cache is shared by every session in the browser profile and outlives sign-out unless wiped.
   - Next.js HTML and RSC payloads carry the layout's personalised data (name, navigation, permissions), and doc 06 forbids caching them.
   - Hard to scope to one person and one purpose. **Rejected.**
2. **Field mode in the app, data in IndexedDB, the service worker unchanged.**
   - A dedicated page loads a snapshot of *only the signed-in person's own open work orders* while online, keeps it in IndexedDB and works from it when the network drops.
   - Changes made offline are queued in IndexedDB and replayed through the normal API, with the normal session, CSRF and permission checks.
   - **Chosen.**
3. **A native app.** Out of scope for R3.

## Decision
- **The service worker rule stays as it is:** no HTML, RSC or API response is cached, and the existing tests keep enforcing it.
- **Field mode** (*Work Orders → Field mode*, `/work-orders/field`) is one client page that keeps working without navigation once opened:
  - **Snapshot:** `GET /api/work-orders/offline` returns at most 50 of the person's own open work orders. Each comes with its checklist and what the screen needs: no costs and no other people's data.
  - **Storage:** the snapshot lives in IndexedDB under the person's user id. It expires after 72 hours and is wiped on sign-out, when another person signs in on the device, and with *Forget this device's data*.
  - **Offline changes:** start, hold, resume, complete, ticking a checklist item, logging time and adding a photo are applied to the screen at once and queued. The queue survives a closed tab.
  - **Replaying:** back online (or on *Sync now*), the queue is replayed in order. Each queued change carries an `Idempotency-Key`; the API remembers keys per person for 7 days, so a change whose answer was lost is never applied twice. Status changes also carry the work order's version, so one changed by someone else in the meantime gets 409.
  - **Conflicts:** a change the server refuses (409, 403, 422) stops the queue for that work order. The page shows the server's reason, with *Try again* and *Discard*. Nothing is merged silently.
- **No tokens or passwords are stored** on the device. Replays need a live session; when it has expired, the person signs in again and the queue resumes.

## Consequences
- Field mode must be opened while online. A full reload while offline shows the static offline page, because HTML is never cached; that page explains this.
- The data on the device is limited to the person's own work orders and expires, but it is **not encrypted at rest**. A key stored next to the data in the same browser would not protect it. Organisations that lend devices should rely on device sign-in and on sign-out wiping the data.
- Photos taken offline are kept as blobs until synced (8 MB each, as online).

## Security review checklist
| Risk | Control |
|---|---|
| Another person on the same device reads the data | Keyed by user id and wiped when someone else signs in; wiped on sign-out; 72-hour expiry |
| Personalised responses in shared caches | The service worker is unchanged; IndexedDB is per origin and read only by the page |
| Replay applies a change twice | `Idempotency-Key`, remembered per person for 7 days |
| A stale offline change overwrites newer work | The version check on status changes; refused changes are shown as conflicts, never forced |
| Escalation through queued requests | Replays go through the normal API with the person's current permissions and session |
