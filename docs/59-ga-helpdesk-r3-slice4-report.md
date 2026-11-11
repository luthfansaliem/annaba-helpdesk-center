# 59 — GA Helpdesk R3 slice 4: visitors

Status: **implemented, verification below**. Fourth slice of R3 (`55-ga-helpdesk-r3-plan.md`). It covers:
- VIS-001: pre-registration;
- VIS-002: reception check-in and check-out, with the block list;
- VIS-005: retention;
- RPT-005 for visitors: the visitor report.

## What was done
1. **My visitors** (everyone signed in; main menu).
   - Register a visitor: name, company, e-mail, phone, vehicle plate, place, purpose, and when they arrive and until when (at most 7 days).
   - Change or cancel the visit while it is still expected.
   - See earlier visits.
   - You are told when your visitor arrives (`visitor.arrived`, also by e-mail).
2. **Reception** (*Service desk → Reception*).
   - **The day's list:** visitors expected on the chosen day plus everyone still on site, searchable by name, company or plate.
   - **Check-in:**
     - a badge number, refused if that badge is with another visitor still on site;
     - the last 4 characters of an ID, when the organisation asks (never the whole number);
     - the organisation's acknowledgement text, which the visitor accepts.
   - **Check-out.**
   - **Walk-ins**, registered for any host by the host's e-mail.
   - **Above the list:** KPIs (expected today, on site, left today, did not come in 30 days, flagged by the block list).
   - **Below the list:** visitors per day for 30 days and by site.
3. **Block list** (*Helpdesk setup → Visitor rules*, `visitors.manage`).
   - **Matching:** an entry matches the same name (spaces and capitals ignored) and, when the entry names a company, the same company.
   - **On a match:** the visit is flagged with the reason and cannot be checked in ("ask General Affairs before letting them in"), and the people who keep the list are told.
   - **Who sees the flag:** reception and managers, not the host.
   - Adding or removing an entry re-checks the visits already expected.
4. **Visitor rules:** the acknowledgement text, whether to ask for ID digits, and how many days visitors' details are kept (7–3650, default 180).
5. **Housekeeping**, with the hourly scheduler:
   - expected visits whose window has passed become *did not come*;
   - finished visits older than the retention period lose the visitor's name (shown as "Visitor"), company, contact, plate, purpose, badge, ID digits and flag. The status and times stay, so the counts stay right.
6. **Receptionist role.** A new system role for reception staff (`visitors.checkin`, `visitors.read_all`), created automatically in existing organisations too. GA Supervisor gains `visitors.read_all`; GA Manager gains `visitors.checkin` and `visitors.manage`.
7. **Audit:** `visit.register`, `visit.update`, `visit.cancel`, `visit.check_in` (badge), `visit.check_out`, `visit.housekeeping`, `visitor.block`, `visitor.unblock`, `visitor.settings`.

## Decisions
- **Registering a visitor needs no permission.** It is self-service like a request (the lesson of doc 56). Reception and the block list need permissions.
- **Only the last 4 ID characters, and only when asked.** Enough to compare at the desk; storing full ID numbers would add a data class the organisation must then protect.
- **The host does not see why a visitor is flagged.** The block-list reason can be sensitive; reception sees it and the people who keep the list are told.
- **Anonymise, don't delete.** The visitor report and the "did not come" counts keep working after the details are gone.

## Tests added
- **API, `VisitTests` (3):**
  - **Registration and reception:**
    - A visit is registered with the plate normalised; invalid input is refused, and a requester cannot register for another host.
    - Only the host, reception and read-all see it: other requesters get 404 and the day list gives 403.
    - Requesters cannot check in; reception cannot change the rules.
    - With an acknowledgement and ID digits required, check-in names every missing field, then succeeds; the host is notified and does not see the ID digits.
    - A badge already with a visitor on site is refused.
    - A visitor on site cannot be cancelled. Check-out works once, after which the badge is free again.
  - **Block list** (isolated organisation):
    - An already expected visit is flagged when the entry is added; a new one is flagged at registration.
    - The host does not see the flag, reception does, and managers are told.
    - Check-in is refused with the reason, and the summary counts both flagged visits.
    - An entry with a company matches only that company.
  - **Housekeeping and summary** (isolated organisation):
    - Retention outside 7–3650 is refused.
    - The summary counts on site and expected today; requesters get 403.
    - The visit nobody came to becomes a no-show; nothing is anonymised before the retention period.
    - Eight days later both visits are anonymised with every personal field empty and the status kept. The 30-day count still includes the visit, and a second run changes nothing.
- **Playwright, `visitors.spec.ts`:**
  - A member registers a visitor in *My visitors*.
  - Reception checks them in with a badge and then out; the host's notifications contain the arrival.
  - Visitor rules: an entry is added to and removed from the block list.
  - The register form, check-in dialog, reception page and visitor rules pass the accessibility check.

## Found and fixed while testing
- **Two older role tests broke on a permission description.** The role form lists every permission with its description, and `visitors.read_all` says "…visitors' names…". The admin and hardening tests looked for the role's *Name* field with a loose match, which now also found that checkbox. They use an exact match now.
- **The organisation test lists every system role.** It now includes *Receptionist*.

## Known gaps
- **No visitor QR pass or gate scanning** (VIS-003, P2).
- **No visitor photo.** VIS-002 lists it as optional; it is left out, like full ID numbers, to keep personal data low.
- **Reception sees every site.** A reception desk per site (scoped roles) can follow when an organisation has several receptions.

## Verification
| Check | Result |
|---|---|
| `tsc --noEmit`, `eslint` | pass |
| `next build` | pass |
| `dotnet test api/AnnabaDigital.sln` | **661 passed, 0 failed** (run together for R3-3 to R3-6) |
| Full Playwright suite | 230 passed, 1 failed (the new inventory test, see doc 61), run together for R3-3 to R3-6 |
