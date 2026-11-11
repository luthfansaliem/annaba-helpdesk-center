# 57 — GA Helpdesk R3 slice 2: the booking lifecycle

Status: **implemented and verified**. Second slice of R3 (`55-ga-helpdesk-r3-plan.md`). It adds the rest of BKG-003 and BKG-005: approval, check-in with no-shows released, reminders, repeating bookings and a private calendar feed.

## What was done
1. **Approval.**
   - A resource can require approval ("Bookings need approval by General Affairs").
   - **What happens to a booking:**
     - A new booking on such a resource is *waiting for approval* and holds its time; the database constraint now covers pending bookings too.
     - An owner who moves an approved booking sends it back for approval.
     - Approvers' own bookings need no approval.
   - **Who is told:** the people who approve (`bookings.manage`, at most 20).
   - **Deciding:** approvers see *Waiting for your approval* on *Bookings* and approve or reject. A rejection needs a reason, and the person is told either way.
   - **No decision in time:** a booking still waiting when it starts is refused automatically ("Not approved before it started").
2. **Check-in and no-shows.**
   - A resource can ask for check-in within 10, 15, 20, 30 or 60 minutes of the start.
   - Check-in opens 15 minutes before the start and shows as a *Check in* button in *My upcoming bookings*.
   - A booking nobody checked in to in time is *released*: the room is free for others and the person is told.
   - The KPI strip counts no-shows over the last 7 days.
3. **Reminders.** 30 minutes before a confirmed booking the person gets a reminder, once, in the app (by e-mail if they switch it on). It includes the check-in rule when the room has one.
4. **Repeating bookings.**
   - *Repeat* every day, every weekday or every week, until a date, at most 52 times.
   - Each occurrence keeps the same local time in the resource's time zone, including across daylight-saving changes.
   - Occurrences that clash or break a rule are skipped and listed. The rest are booked together.
   - Each occurrence can be changed on its own. Cancelling offers *Only this one* or *This one and all after it*.
5. **Calendar feed.**
   - *Calendar feed* on *Bookings* makes a private link (`…/api/calendar/<secret>.ics`) to subscribe to in Outlook, Google Calendar or Apple Calendar.
   - It holds your pending and confirmed bookings from 30 days ago to 6 months ahead, with text escaped and lines folded as RFC 5545 requires.
   - **The secret:** the link is shown once and stored only as a SHA-256 hash. Making a new link retires the old one, and *Turn the link off* ends it.
   - **What the calendar app sees:** it fetches the link without a session; an unknown token or an inactive account gets 404. The page shows when a calendar last read it.
6. **Background worker.** A new `BookingWorker` runs every minute, because reminders and releases cannot wait for the hourly scheduler. It claims every item in one UPDATE, so several API instances never act twice. It follows the same on/off switch as the other schedulers.
7. **Notifications.**
   - New kinds: `booking.approval` and `booking.decided` (e-mail on by default) and `booking.reminder` (in the app only by default).
   - All of them are listed under *Notifications → Settings → Bookings*.
8. **Audit:** `booking.series`, `booking.approve`, `booking.reject`, `booking.check_in`, `booking.release`, `booking.expire`, `booking.feed_create`, `booking.feed_revoke`. `booking.cancel` now records the scope and the count.

## Decisions
- **A pending booking holds its slot.** Otherwise two people could each get "waiting for approval" for the same hour, and the approver would have to refuse one.
- **Check-in is per resource.** Rooms that are often booked and left empty use it; vehicles and equipment usually do not.
- **The feed is read-only, per person and revocable.** It never shows other people's bookings. A leaked link is fixed by making a new one.
- **A series is not a separate table.** Occurrences share a `series_id`. Exceptions are simply occurrences that were moved or cancelled, which keeps every booking an ordinary booking for the overlap rule, reports and the timeline.

## Tests added
- **API, `BookingLifecycleTests` (4):**
  - **Approval:**
    - A requester's booking is pending, approvers are notified, and the slot is held.
    - Only `bookings.manage` sees and decides; a rejection needs a reason.
    - Approval confirms the booking and notifies the person; deciding twice is 409.
    - A rejected booking frees the slot, and an approver's own booking is confirmed directly.
    - An approved booking moved by its owner waits again.
  - **The background run** on an isolated organisation with a moving clock:
    - Reminders reach exactly the confirmed bookings starting within 30 minutes, and only once.
    - Check-in is refused before it opens, works 10 minutes before the start, and cannot happen twice or on a resource without check-in.
    - A pending booking whose start passes is refused.
    - A booking nobody checked in to is released once, its time can be booked again, and the summary counts the no-show.
  - **Repeating bookings:**
    - A weekly series of four with one week already taken books three and lists the skipped one with its reason.
    - *This one and all after it* cancels from the second on.
    - A weekday series has no weekend dates.
    - More than 52 occurrences, no end, or an unknown repetition are refused.
  - **Calendar feed:**
    - Off at first; made; fetched without a session as `text/calendar` with the escaped summary, the UTC start and every line at most 75 bytes.
    - A new link retires the old one, turning it off ends it, and a short token gets 404.
    - Another person's feed does not contain your bookings.
  - The anonymous-endpoint test now lists `/api/calendar/{token}` with the reason.
- **Playwright, `bookings.spec.ts`:**
  - A member's booking of a room that needs approval shows *Waiting for approval*; the administrator approves it from *Waiting for your approval*.
  - The member makes a calendar link, which a client without a session can fetch, and which returns 404 after *Turn the link off*.
  - The bookings page with approvals and the feed dialog pass the accessibility check.

## Known gaps
- **Microsoft 365 and Google calendar sync** (writing into people's calendars) stays P2; the feed covers reading.
- **No-show policy.** Releasing a no-show counts it, but nothing else happens yet, such as blocking frequent no-shows.
- **Late cancellations** are allowed and recorded like any cancellation; there is no minimum notice rule.

## Verification
| Check | Result |
|---|---|
| `tsc --noEmit`, `eslint` | pass |
| `next build` | pass |
| `dotnet test api/AnnabaDigital.sln` | 650 passed, 1 failed: `SetupTests.The_quick_start_keeps_what_the_organisation_already_has…`, which passed when run alone and in every earlier full run (it sets up a fresh organisation and is unrelated to these changes; noted as load-sensitive); the booking tests (10) also passed three runs in a row on their own |
| Full Playwright suite | **227 passed, 0 failed** (with doc 54 and R3-1) |
| Seen in the browser (admin, dev data) | approvals panel and KPI, pending booking dashed orange on the timeline, repeat fields, calendar feed made and fetched through the web proxy without a session |
