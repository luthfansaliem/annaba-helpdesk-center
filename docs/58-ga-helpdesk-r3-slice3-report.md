# 58 — GA Helpdesk R3 slice 3: vehicles and utilisation

Status: **implemented, verification below**. Third slice of R3 (`55-ga-helpdesk-r3-plan.md`). It covers:
- BKG-004: vehicle trips, vehicle documents and driving licences;
- RPT-005 for rooms and vehicles: the utilisation report.

It also brings trip costs into the cost report.

## What was done
1. **The trip of a vehicle booking** (*Trip* on a vehicle booking in *My upcoming bookings*, or in the booking form).
   - **Before:**
     - Destination and purpose.
     - Who drives: the person it is booked for, or *I need a driver from General Affairs*, which alerts the managers.
     - Managers pick a driver from the people with a licence on record, valid licences first. A driver whose licence has expired by the start is refused, and the assigned driver is told.
   - **Pick-up**, from 30 minutes before the start:
     - the odometer reading, which may not be below the car's last return reading;
     - picking up counts as checking in.
   - **Return:**
     - the odometer (not below the pick-up reading);
     - fuel, tolls and parking paid;
     - a five-item checklist: fuel level, clean, no new damage, keys and card, STNK in the car;
     - damage, which must be described when "no new damage" is unticked, and which alerts the managers.
     - Returning early ends the booking at the next 5-minute step, so the car is free again.
     - A returned trip is final.
   - **Who handles it:** the people booked for or by, the driver (who now sees the booking among their own), or `bookings.manage`.
2. **Vehicle documents** (*Bookable resources → Documents* on a vehicle): STNK, insurance, KIR, service due or other, with number and expiry date, shown as valid, expires soon (30 days) or expired.
3. **Driving licences** (*Bookable resources → Driving licences*): recorded by the driver's e-mail, with number, class and expiry.
4. **Reminders:** the people who keep resources get a reminder 30 days before and on the day a document or licence expires; the driver also gets one about their own licence. Each stage is sent once and starts over when the date changes. The reminders run with the hourly scheduler.
5. **Utilisation report** (*Insights → Utilisation*, `bookings.read_all`).
   - For 30 days to 12 months, filtered by kind, per resource:
     - opening hours, and the hours booked inside them, as a share;
     - bookings, no-shows and cancellations;
     - for vehicles, trips, kilometres and trip costs.
   - KPIs at the top, plus the most and least used resources: candidates to give up or share.
6. **Trip costs in the cost report.**
   - Fuel, tolls and parking are a new kind of cost, *Vehicle trips*. They count on the day the car came back, at the vehicle's place, and for the person it was booked for, so they appear by site, cost centre and department like any other cost.
   - By category or service they show as "Vehicle trips". They count in the total but not as work orders.
   - The cost page shows the kind as a column, a chart series, a donut slice and a KPI (when there are any). The CSV gains a column.
7. **Notifications:** `vehicle.alert` (a driver is needed or damage was reported) and `resource.document` (expiries), both under *Notifications → Settings → Bookings*.
8. **Audit:** `trip.update`, `trip.pick_up`, `trip.return` (km, cost, damage yes/no), `resource.document_add/update/remove`, `driver.licence_save/remove`.

## Decisions
- **The trip belongs to the booking** (one row per booking, deleted with it). Bookings stay the single source of who has the car when; the trip adds what happened on the road.
- **Licences are optional data.** A driver without a licence on record can still be assigned, but the picker lists recorded licences first and marks expired ones. Refusing only expired licences avoids blocking organisations that have not entered any yet.
- **Costs on the day the car comes back.** That is when the receipts are known; a trip spanning a month end counts in the month it ended.
- **Early return frees the car;** a late return does not stretch the booking (the next booking may already exist).

## Tests added
- **API, `VehicleTests` (4, on isolated organisations; the fourth races saving a trip against picking up):**
  - **The trip, end to end:**
    - Asking for a driver; a requester cannot assign someone else.
    - Licences recorded by e-mail, and an unknown e-mail is refused.
    - The driver picker is for managers only and lists valid licences first.
    - An expired licence is refused for the trip; the assigned driver is notified.
    - Pick-up without a reading, return before pick-up, and picking up twice are refused; picking up checks in.
    - Return refuses an odometer below pick-up, a missing checklist, damage without a description and negative costs.
    - The return shows distance 320 km and cost Rp 350,000 and records the failed checklist item. The early return ends the booking, and a returned trip is final.
    - The next trip refuses a reading below 15,320 km and shows it as the last reading.
    - The cost report has a "Vehicle trips" row with the vehicle amount and no work orders, the CSV has the column, and the damage alert reached the managers.
    - A room booking has no trip.
  - **Documents and licences:**
    - Requesters get 403 and invalid input is refused.
    - States: expired, soon and ok.
    - The first run sends 3 reminders; the second sends none, and the driver hears about their own licence.
    - Eleven days later the expired stage is sent once.
    - A new date resets the stage, and removing a document works.
  - **Utilisation:**
    - Requesters get 403.
    - 2 booked hours against 168 opening hours over 7 days, 1 booking and 1 cancelled.
    - Vehicles are listed, the room filter keeps only rooms, and the totals are right.
- **Playwright, `bookings.spec.ts`:**
  - A member opens *Trip* on a pool-car booking, saves the destination, picks up at 1,000 km and returns at 1,050 km with fuel and the checklist; the summary shows 50 km.
  - GA's utilisation report lists the car with its kilometres.
  - The trip drawer and the report pass the accessibility check.

## Found and fixed while testing
- **Messages followed the server's culture.** The odometer message read "15.320 km" on a server set to Indonesian. All API messages are English, so numbers and dates in them are now formatted invariantly ("15,320 km", "Thu 12 Nov"). The same applies to the booking messages.
- **Typing was lost after saving a trip.** The trip form re-seeded its fields every time the trip reloaded, wiping an odometer reading typed right after *Save trip*. It now seeds only when a different trip opens.
- **Saving a trip and picking up at the same moment gave a 500.** Both requests found no trip row and both inserted one. The trip row is now created safely (a losing insert re-reads the winner's row), and two changes colliding get 409 instead of a server error. A new API test runs the race three times.
- **The least-used chart showed 0.1% for unused resources** (zero values were padded so the bars would draw). It now shows the real value, and an explanation when nothing was booked.

## Known gaps
- **Driver on the booking form.** The driver is assigned on the trip, not while booking; a booking made by GA for a driver uses *book for someone* in the API only.
- **No fuel-card or receipt upload yet.** Receipts can be attached later; the amounts are typed.
- **Vehicle service history.** The service *due date* is a document; actual services are work orders on the asset, not linked to the bookable vehicle yet.

## Verification
| Check | Result |
|---|---|
| `tsc --noEmit`, `eslint` | pass |
| `next build` | pass |
| `dotnet test api/AnnabaDigital.sln` | **661 passed, 0 failed** (run together for R3-3 to R3-6) |
| Full Playwright suite | 230 passed, 1 failed (the new inventory test, see doc 61), run together for R3-3 to R3-6 |
