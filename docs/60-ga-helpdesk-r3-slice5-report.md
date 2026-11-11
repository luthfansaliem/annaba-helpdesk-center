# 60 — GA Helpdesk R3 slice 5: mail and courier

Status: **implemented, verification below**. Fifth slice of R3 (`55-ga-helpdesk-r3-plan.md`). It covers:
- VIS-004: the incoming and outgoing mail and package log;
- VIS-005: retention for mail.

## What was done
1. **Mail & parcels** (main menu, everyone).
   - **Everyone:** sees the letters and parcels reception logged for them (incoming) or sent for them (outgoing), with status, courier, waybill and who collected them.
   - **The mailroom** (`mail.manage`):
     - the same page with every item;
     - KPIs: waiting, waiting over 2 days, received and collected today, outgoing to send and sent this week;
     - incoming per day for 30 days, and items by courier.
2. **Incoming:**
   - Logged for an employee by e-mail, with sender, courier, waybill and description.
   - The employee is told at once ("A parcel for you is at reception"), and reminded once if it is still waiting after two days.
   - *Collected* records who took it (the recipient by default, or a colleague's name).
3. **Outgoing:**
   - Logged for the employee sending it, with addressee and address.
   - *Send* records the courier and waybill, and the employee is told it has left.
4. **Mistakes** are voided with a reason; voided items disappear from the person's list.
5. **Retention.** After the visitor retention period (doc 59; default 180 days), finished items keep their kind, status, dates and courier but lose the sender or addressee, address, waybill, description, collector and note.
6. **Permissions:** `mail.manage` for *Receptionist* and *GA Manager*. Seeing your own mail needs no permission.
7. **Notifications:** `mail.arrived` (e-mail on by default) and `mail.sent` (in the app).
8. **Audit:** `mail.receive`, `mail.register`, `mail.collect`, `mail.send`, `mail.void`, `mail.housekeeping`.

## Decisions
- **Collection is recorded by name, not photo.** VIS-004 allows "signature/photo proof". A name is enough for most mailrooms and keeps personal data low; photo proof can be added on the same item later.
- **One retention period for visitors and mail.** Both are reception records of outsiders; one setting is easier to explain.
- **The courier is free text.** Couriers vary by region (JNE, J&T, SiCepat, Pos Indonesia, private ones); the KPI groups by what reception typed.

## Tests added
- **API, `MailTests` (1, isolated organisation):**
  - **Logging:**
    - Requesters cannot log mail, and invalid direction, kind and person are refused.
    - Outgoing mail needs an addressee.
    - Incoming mail is logged with the waybill normalised, and the recipient is notified with the exact title.
  - **Visibility:** the recipient sees only their item and cannot handle it, and others see nothing. Requesters cannot use the desk view, and the desk finds the item by part of the waybill.
  - **Outgoing:** sending needs a courier and a waybill, then it is sent and cannot be collected.
  - **Voiding** needs a reason; the voided item no longer counts for the person.
  - **Summary:** waiting, received today and sent this week are counted. Requesters get 403; managers may read it.
  - **Two days later:** exactly one reminder, the "waiting over 2 days" and oldest-days KPIs, collection by a colleague's name, and collecting twice refused.
  - **After 181 days:** all three finished items are blanked, the statuses are kept, and a second run changes nothing.
- **Playwright, `visitors.spec.ts`:**
  - The administrator logs an incoming parcel for the member in the mailroom.
  - The member sees it waiting in *My mail* (without the mailroom buttons).
  - Marking it collected removes it from "still at reception", and the member sees it as collected.
  - The log form and the mailroom page pass the accessibility check.

## Known gaps
- **No photo or signature capture** at collection.
- **No courier tracking links.** Waybills are text; tracking pages differ per courier.
- **No locker or shelf position** for parcels waiting.

## Verification
| Check | Result |
|---|---|
| `tsc --noEmit`, `eslint` | pass |
| `next build` | pass |
| `dotnet test api/AnnabaDigital.sln` | **661 passed, 0 failed** (run together for R3-3 to R3-6) |
| Full Playwright suite | 230 passed, 1 failed (the new inventory test, see doc 61), run together for R3-3 to R3-6 |
