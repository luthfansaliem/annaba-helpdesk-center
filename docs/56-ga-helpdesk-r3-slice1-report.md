# 56 — GA Helpdesk R3 slice 1: bookable resources and booking

Status: **implemented and verified**. First slice of R3 (`55-ga-helpdesk-r3-plan.md`). It covers:
- BKG-001: bookable resources;
- BKG-002: no double booking, enforced by the database;
- BKG-003: day and week views, and search by time, size and feature.

## What was done
1. **Bookable resources** (*Helpdesk setup → Bookable resources*, `resources.manage`).
   - **Kinds:** meeting rooms, vehicles, parking slots and shared equipment.
   - **Each resource has:**
     - a code (or plate number), unique per organisation;
     - a place;
     - seats;
     - features (projector, video call, whiteboard, EV charger…);
     - opening days and hours, or around the clock;
     - a buffer kept free after each booking, for cleaning or handover;
     - the longest booking allowed;
     - optionally, the roles that may book it.
   - **Archiving:** archived resources keep their history. A resource with upcoming bookings cannot be archived until they are cancelled or moved.
2. **Booking** (*Bookings* in the main menu, for everyone signed in). The page shows, from top to bottom:
   - **A KPI strip:** free now, in use now, bookings today and in the next 7 days, my upcoming, and room and vehicle use over the last 7 days (booked hours ÷ opening hours).
   - **Tabs per kind**, with filters for place, size ("at least … people") and feature.
   - **A day timeline of all matching resources**, or a week of one resource:
     - free time is clicked to book, and the form opens prefilled with that resource and the half hour;
     - closed hours and days are hatched, and the buffer after each booking shows as a striped strip;
     - a red line marks now;
     - your own bookings are solid blue.
   - **My upcoming bookings**, with *Change* and *Cancel*.
3. **Rules checked by the server.**
   - Not in the past (a 15-minute grace lets you book a meeting that just started).
   - At most 365 days ahead.
   - On whole 5-minute steps.
   - Within the resource's longest booking (14 days when it has none).
   - Inside its opening days and hours, in the resource's own time zone (the place's, else the organisation's default calendar). A vehicle open around the clock can be booked across midnight.
   - No more people than it holds.
   - Only by the roles it allows.
   - A title is required.
4. **No double booking, even under a race.**
   - **The application check:** the server first looks for a clash and answers `409` with the clashing time, buffer included, e.g. "Garuda meeting room is already taken from Thu 12 Nov 09:00 to 10:40". It never says who booked it or why.
   - **The database guarantee:** an exclusion constraint on the resource and the `[start, end + buffer)` range of every live booking.
   - **When two requests race:** the database refuses one of them, either as a constraint violation or by aborting it as a deadlock. Both outcomes are answered `409`.
5. **Privacy.**
   - People who are not involved see only that a time is taken: no title, person, note or headcount.
   - `bookings.read_all` sees everything.
   - `bookings.manage` also books for colleagues and changes or cancels anyone's booking. Cancelling someone else's booking requires a reason, and that person is notified ("Your booking of … was cancelled — reason").
   - Someone booked for by a colleague is notified too.
   - The notification kind `booking.changed` (in the app and by e-mail) can be switched off under *Notifications → Settings → Bookings*.
6. **Audit:**
   - resources: `resource.create`, `resource.update`, `resource.archive`, `resource.restore`;
   - bookings: `booking.create`, `booking.update` (with `moved` when the time or resource changed), `booking.cancel`.
7. **Demo data.** `scripts/demo-data.mjs` adds three rooms, a pool car, a parking slot and a projector, and eight bookings over the next weekdays. Running it again adds nothing new.

## Permissions
| Permission | Held by default | What it allows |
|---|---|---|
| — (signed in) | everyone except vendor accounts | Book for yourself; change and cancel your own bookings |
| `bookings.read_all` | GA Supervisor and up | See who booked what and why |
| `bookings.manage` | GA Supervisor and up | Book for others; change or cancel anyone's booking (with a reason); book role-restricted resources |
| `resources.manage` | GA Manager | Maintain the bookable resources |

Booking for yourself needs no permission, like making a request. A first version had a `bookings.create` permission on the *User* role; the full API suite showed why that is wrong (see below).

**Upgrade note for existing organisations:** everyone can book straight away. The roles already in the database keep their permissions as tuned, so after deploying an administrator adds `bookings.read_all`, `bookings.manage` and `resources.manage` on *Access Matrix* (doc 22 says to which roles). Until then only the super admin can add resources.

## Decisions
- **The database decides overlaps.** The friendly check alone cannot stop two people pressing *Book* at the same moment. The `EXCLUDE` constraint can, and the race test proves it.
- **The buffer is fixed at booking time.** `blocked_until` stores end + buffer. Changing a resource's buffer later does not move existing bookings.
- **Times shown are the device's.** The timeline and form use the browser's time zone, while the server checks opening hours in the resource's own zone. For an organisation in one time zone the two are the same; the page says which zone is shown.
- **Approval waits for R3-2.** `needsApproval` is stored but not yet enforced. The resource form does not offer it yet.

## Found and fixed while testing
- **Notifications were never saved.** `NotificationService.SendAsync` adds rows and leaves saving to the caller, but bookings sent them after saving. They are now added before the save, and are dropped with it when the save fails.
- **Local offsets reached the database.** The availability and summary queries passed times with a +07:00 offset, which Npgsql refuses for `timestamptz` (500). All query bounds are now UTC.
- **A lost race sometimes gave 500.** Two simultaneous inserts can deadlock (40P01) instead of violating the constraint, and EF reports that as an `InvalidOperationException` around the `DbUpdateException`. The losing request now gets `409` like the constraint case. The race test ran 18 races in a row without a failure.
- **A permission on the *User* role broke identity rules.** The first version gave *User* `bookings.create`. The full API suite failed 33 identity tests, because the role hierarchy, delegated invitations and access reviews treat any role holding a permission as privileged, and "User holds nothing" is a base rule. Booking for yourself now needs no permission, like making a request; vendor accounts are kept out by the vendor portal gate, which a new test checks.

## Tests added
- **API, `BookingTests` (6):**
  - **Resources:** maintained only with `resources.manage`, validated field by field, features normalised, code unique, found by capacity and feature, edits versioned. A vendor's person gets 403 on resources and bookings, and anonymous calls get 401.
  - **Booking rules:** buffer, overlap refused with a message, back-to-back after the buffer allowed, before opening, after closing, closed day, off-step times, too long, too many people, the past, an empty title, moving a booking without clashing with itself.
  - **The database under a race:** three rounds of two simultaneous overlapping bookings; each time exactly one is created and one is refused, and three bookings remain.
  - **Privacy and who may change:** others see no title, person or note. `bookings.read_all` does. Others get 404 on read, change and cancel, and 403 on `scope=all`. Only a manager books for someone else. A manager's cancel needs a reason and notifies the person. Cancelling twice is 409, and the slot is free again afterwards.
  - **Role-restricted resources and archiving:** restricted to GA Agent, refused for a requester, a vehicle trip across midnight, archive refused while booked and allowed after cancelling, an archived resource hidden and unbookable, then restored.
  - **The summary** on an isolated organisation: resources, in use now and free now, my upcoming, the next 7 days and resources per kind.
- **Playwright, `bookings.spec.ts`:**
  - GA adds a room around the clock on the setup screen.
  - A member books it from *Bookings*, it shows in *My upcoming bookings* and on the day timeline.
  - The same slot is refused through the API with "already taken".
  - The member cancels it and the slot can be booked again.
  - The resource form, the resources page and the bookings page pass the accessibility check.

## Known gaps
- **Approval, check-in, no-show release, reminders, cancellation rules, recurring bookings and the calendar feed** come in R3-2.
- **Vehicle trip details** (driver, odometer, fuel, return checklist) come in R3-3.
- **Booking on someone's behalf from the screen.** The API supports it for `bookings.manage`, but the form has no person picker yet.
- **The week view shows one resource at a time.**

## Verification
| Check | Result |
|---|---|
| `tsc --noEmit`, `eslint` | pass |
| `next build` | pass |
| `dotnet test api/AnnabaDigital.sln` | **647 passed, 0 failed** with doc 54 (+6 `BookingTests`); with R3-2: 650 passed, 1 failed: `SetupTests.The_quick_start_keeps_what_the_organisation_already_has…`, which passed when run alone and in every earlier full run (it sets up a fresh organisation and is unrelated to these changes; noted as load-sensitive) |
| Full Playwright suite | 225 passed and 1 failed in the first run: the timeline bar's accessible name did not start with its visible text (axe `label-content-name-mismatch`, WCAG 2.5.3). Fixed; **227/227** in the run with R3-2 |
| Seen in the browser (admin, dev data) | day timeline with demo bookings at 1440 px and in the narrow pane; free-slot click opens the form prefilled; a clash shows the server's message; resources list and edit form |
