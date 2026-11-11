# 54 — GA Helpdesk: periods on module summaries, satisfaction trend, dark mode

Status: **implemented and verified**. This slice closes the open items listed under "Known gaps" in `53-ga-helpdesk-ui-redesign-report.md`:
- the module summaries had fixed windows;
- satisfaction over time and time to complete per category were not charted;
- dark mode had not been reviewed screen by screen.

## What was done
1. **A period on the module summaries.**
   - Work orders, vendors and maintenance now show *At a glance* with a period picker: last 30 days, last 90 days, last 6 months or last 12 months.
   - The API takes `?days=` (7 to 365, clamped) and echoes the period back (API spec §12.20).
   - The period applies only to figures that happened in time: finished work, average time to complete, compliance, ratings and spend. What is open, overdue or due soon is always "now", and the line under *At a glance* says so.
   - Defaults stay close to before: work orders 30 days, maintenance 90 days, vendors 12 months.
   - A longer period also widens the trend:
     - work orders show up to 53 weeks instead of 12;
     - maintenance shows up to 12 months instead of 6.
   - Assets and the knowledge base are snapshots and keep no period.
   - Field names changed with the meaning:
     - `done30`/`onTime30`/`late30` → `done`/`onTime`/`late`;
     - `spend12Months` → `spend`;
     - `last90` → `period`.

     The web app is the only client and was updated with them.
2. **Time to complete by category.**
   - The work-order summary adds `byCategory`: the average time from creation to completion per service category, for work finished in the period.
   - It lists up to 10 categories, slowest first, with how many were finished.
   - It is shown as bars under the work-order charts. Times under an hour read "< 1 h". When everything was finished within the hour, the card says so instead of "nothing to show".
3. **Satisfaction trend.**
   - The satisfaction report returns `trend`: ratings per week for periods up to 90 days, per month beyond. Each step has its count, satisfied (4–5), low (1–2), average and satisfied share.
   - Empty weeks and months are kept, so a gap shows as a gap.
   - The page shows them as stacked columns (satisfied, neutral, low) with a table of the averages under the chart.
   - Its period picker now also offers 6 and 12 months.
4. **Dark mode review.**
   - **How it was checked.** Every main screen was opened in dark mode as administrator: home, queue, ticket, new request, my requests, knowledge base, work orders, assets, vendors, maintenance, compliance, operations, SLA, satisfaction, cost report, budgets, catalog, SLA policies, assignment rules, access overview, users, roles, audit, profile, settings, notifications, approvals and help. A script also checked each page for light surfaces (background luminance above 0.6) and for near-black text.
   - **What was found.** One problem: the coloured icon squares of the account statistics on *Access overview* and *Users* kept their light pastel backgrounds.
   - **The fix.** In dark mode they are now tinted from their own icon colour.
   - **Flagged but intended.** The white count badge on the active quick view of the queue was the only other light surface. It is blue on white by design (decided in doc 53 for contrast).
   - **Light mode.** The new parts were checked in the light theme too.

## Decisions
- **One period picker per module, not one for the whole app.** The modules answer different questions: 12 months suits vendors, 30 days suits work orders. Each starts at the window that fits it.
- **"Now" figures ignore the period.** A late work order is late whatever period is picked; mixing the two would make "Overdue" change when the picker changes.
- **Weekly up to 90 days, monthly beyond** for the satisfaction trend. That keeps the chart between 2 and 14 columns, which stay readable.

## Tests added
- **API, `InsightsTests`:**
  - The main test now also checks:
    - the default periods (30 and 90);
    - `?days=365` (53 weeks, 12 months);
    - clamping (`?days=1` gives 7).
  - The work-order time per category is counted from the finished work order.
  - **40 days later:**
    - the finished work order has left the 30-day window but is in the 90-day one;
    - the vendor's 30-day ranking still counts its open work but not the finished one;
    - the 30-day spend is 0 while the 12-month default still has it.
  - **New: `The_satisfaction_trend_is_weekly_up_to_90_days_and_monthly_beyond`:** weekly steps from the period's first day, empty steps kept, monthly steps across a half year.
- **Playwright:**
  - **`csat.spec.ts`:** the satisfaction report shows *Ratings per week* with the averages table, then *Ratings per month* after choosing 12 months.
  - **`home-insights.spec.ts`:** the work-order summary follows the period picker ("from creation · last 30 days" → "last 12 months").

## Known gaps
- The period choice is not remembered between visits; each page opens at its default.
- Operations and SLA reports keep their 7/30/90-day choices; only satisfaction gained 6 and 12 months.

## Verification
| Check | Result |
|---|---|
| `tsc --noEmit`, `eslint` | pass |
| `next build` | pass |
| `dotnet test api/AnnabaDigital.sln` | **641 passed, 0 failed** on its own (640 before; +1 trend test); 647/647 together with R3-1 |
| Full Playwright suite | 224 passed and 1 failed on its own run: the new satisfaction check used `getByLabel("Period")`, which also matched the new table "Average rating per period". Fixed to an exact match; **227/227** in the run with R3-1 and R3-2 |
| Dark-mode scan of 28 screens (administrator) | 1 problem found and fixed (icon squares); no near-black text |
