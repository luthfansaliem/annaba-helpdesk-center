# 53 — GA Helpdesk: UI redesign and module analytics

Status: **implemented and verified**. The review (`52-ga-helpdesk-ui-review.md`) found that the frame was still the identity-and-access template, that only one module had analytics, and that the layout was messy. This report covers all seven steps of its plan; the verification table is at the end.

## What was done
1. **Helpdesk identity.**
   - The product name is now "Annaba Helpdesk" in the browser tab, the installable app (manifest name and shortcuts) and on the sign-in page.
   - The sign-in page says what the app is for: "Ask once, we take it from there".
2. **The menu follows the work.**
   - Top level: *Home*, *New request*, *My requests*, *Knowledge base*, *Approvals*.
   - Groups: *Service desk* (queue, work orders), *Assets & vendors* (assets, maintenance, vendors), *Insights* (operations, SLA, satisfaction, maintenance compliance, cost report, budgets), *Helpdesk setup* (now including cost centres), *People & access* (access overview, users, roles, reviews, closures, audit, security events), *Administration*, and *Personal* (profile, notifications, access requests, sessions, settings, help).
   - Requesters see only the top level and *Personal*.
   - A page linked from two places opens the group of its exact link: */maintenance/compliance* opens *Insights*.
3. **Home pages by role.**
   - **People who work the queue** (`tickets.read_team`) get:
     - six KPIs: open, unassigned, at risk, overdue, average resolution, SLA attainment;
     - *My tickets*, with the soonest deadline first;
     - the backlog by status;
     - created vs resolved per day;
     - their work orders.
   - **Everyone else** gets:
     - *What do you need?*, one search box over the services and the knowledge base;
     - *Popular services* as tiles;
     - *My open requests*;
     - a way into the knowledge base.
   - **A vendor's person** still lands on their work orders.
   - **The account and security overview** that used to be the home page is now *People & access → Access overview* (`/access`). Its profile checklist shows readable names instead of field keys.
4. **One page pattern for every module.**
   - The order is: header with the primary action, a KPI strip, charts, a compact filter bar, then the table.
   - The KPI strip fits as many columns as there is room for, so there is never a lonely stretched card.
   - The filter bar is one wrapping row; check-boxes are shown as chips.
   - A module with no records shows what it is for and offers the first step (*Add the first asset*, *Import from CSV*, *Add a vendor*, *Create the first plan*).
5. **Module analytics.** Five new read-only endpoints (`/api/{work-orders,assets,vendors,maintenance,kb}/summary`, API spec §12.20). Each uses the module's read permission and the same scope as its list.

| Module | What it shows |
|---|---|
| Ticket queue | Open, unassigned, at risk, overdue, resolved (30 days); quick views with counts (all open, assigned to me, unassigned, at risk, overdue, on hold, waiting for approval); place and dates behind *More filters* |
| Work orders | Not finished (in-house vs vendors), overdue, due this week, done in 30 days with the on-time share, average time to complete; created vs done per week (12 weeks); open work by status and the vendors with the most open work |
| Assets | In service, in repair, warranties ending within 30 days and already ended, maintenance due, purchase value, assets without a place; by status, type and site; assets with the most requests |
| Vendors | Active and blocked vendors, contracts in force and those ending within 60 days, open and late vendor work, spend over 12 months (only with `workorders.costs`); a performance table with on-time share, rating and spend |
| Maintenance | Active and paused plans, due in 7 and 30 days, overdue visits, compliance over 90 days, missed visits; visits per month (on time, late, open, missed); the next plans due |
| Knowledge base (writers) | Published articles and drafts, reads, requests avoided, helpful share, articles due for review; most read; articles per category |
| Cost report | Total, parts, labour, services and vendor invoices in Rupiah; cost per month for every month of the period (stacked by kind) or the largest groups; where the money went |
| Budgets | Budgeted, spent so far, near the limit, used up |
| Satisfaction | Average per team, agent, site or vendor as bars; the period picker moved to the header |
| SLA report | Attainment by priority and by team as coloured bars |

6. **Ticket page.**
   - Two columns: details, rating, approval, the conversation with the reply box, work orders and attachments on the left; actions, summary, service level and legal hold on the right.
   - The header shows the ticket number, service, subject and badges.
   - The back link goes to the queue for staff and to *My requests* for requesters.
   - Files are added through a drop zone.
   - The internal-note switch and *Send comment* sit on one line.
7. **Polish.**
   - **Layout.** The `row` class is used about 170 times for "side by side" but never had a style, so everything inside it stacked. It now has one, which fixes the stacked filters, the "General Affairs24" bar labels and the buttons that touched each other.
   - **Catalog.** Services are tiles with category icons and colours.
   - **Money.** Rupiah is shown without decimals (`Rp 1.250.000`), or short in charts and KPI tiles (`Rp 15,9 jt`).
   - **Tables.** Ticket numbers and status pills no longer wrap.
   - **Line chart.** The curve no longer overshoots below zero, and the first and last x-labels are no longer cut off.
   - **Hydration warning.** The warning on every page was the theme script's CSP nonce: browsers hide it after parsing, so React saw a mismatch. The warning is now suppressed for that one element only.
   - **Colour variables.** The light theme's `--ok-line` and `--err-line` pointed at themselves; they now have values.
   - **Translations.** All 169 new strings have Bahasa Indonesian.
8. **Richer demo data.** `scripts/demo-data.mjs` now also fills every other module:
   - a currency and labour rate;
   - 5 asset types and 11 assets (some in repair, warranties ending and ended);
   - 3 vendors with contracts;
   - work orders on the demo tickets, with costs and time, some finished;
   - 3 maintenance plans;
   - knowledge-base articles with reads, votes and "this solved it";
   - ratings on resolved tickets;
   - a site budget.

   It is safe to run twice. A step that fails on an unusual setup is reported and skipped.

## Decisions
- **Home depends on what you do, not on a setting.** `tickets.read_team` decides between the agent home and the requester home. Managers use *Insights → Operations* for the full report.
- **Summaries are separate endpoints.** The list endpoints stay as they were (paged, filtered); a summary is one small request.
- **Money stays behind its permission.** The vendor spend is `null` for people without `workorders.costs`, as on the vendor's own page.
- **No backdating in the demo data.** It goes through the public API, so everything it creates happens "now". The weekly and monthly charts fill up as the system is used.

## Tests added
- **API, `InsightsTests` (1):** on an isolated organisation, each summary counts exactly what was created.
  - Assets by status, warranty windows, place, value, type and site (a floor counts for its site).
  - Work orders: open, overdue after the clock moves, done on time, in-house vs vendor, 12 weekly points, by vendor.
  - Vendors: blocked, contracts ending within 60 days, open and late work, spend. The spend is `null` for an agent without `workorders.costs`.
  - Maintenance: active, paused, due within 7 and 30 days, monthly rows, upcoming.
  - Knowledge base: published, drafts, reads, helpful, deflections, top article.
  - A requester gets `403` from all five.
- **Playwright, `home-insights.spec.ts` (2):**
  - A requester's home asks what they need, finds a service as they type, lists their requests, and shows none of the staff groups. The catalog is tiles.
  - Staff home KPIs and *My tickets*. Queue quick views set the status, and *More filters* opens the date fields.
  - Each module page shows its KPI strip, or the start-here card when it is empty.
  - *Access overview* is reached and highlighted in the menu.
  - Every one of these pages passes the accessibility check.
- **Updated expectations:**
  - The menu group names in `admin.spec.ts` and `navigation.spec.ts`.
  - "Beranda" instead of "Dasbor" in the Indonesian check.
  - The cost line in `costs.spec.ts` now reads `Rp 900` instead of `900.00`.

## Known gaps
- **Charts need history.** On a new organisation the trends are flat until work accumulates.
- **No date ranges on module summaries.** They use fixed windows (30/90 days, 12 weeks, 6 months); a period picker can follow if the managers want one.
- **CSAT and work-order time trends.** Satisfaction over time and time to complete per category are not charted yet.
- **Dark mode was not reviewed screen by screen.** The new styles use the theme variables throughout.
- **Production needs a redeploy.** It runs the code from before this change, and the demo data is for development and staging only.

## Found and fixed while testing
- **Old tests that read the ticket number from the page title.** The title had become just the subject, which broke two of them. It is back to "number · subject", with the number in blue.
- **Tests that relied on the old menu.** The session-expiry test clicked a "Dashboard" link that is now "Home". The notification-settings test now looks for its *Settings* link inside the page, because the open *Personal* group has a Settings link of its own.
- **Quick-view badge contrast.** The count on the active quick view was white on translucent white over blue and failed the contrast check. It is now blue on white.
- **Cost test.** It now matches `Rp 900` (no decimals) without confusing it with the work-order count next to it.
- **SLA report test depended on the time of day.** It put tickets "30 days ago", but the report window starts at midnight 29 days ago. Early in the UTC day the breach fell outside the window, so the test failed on its own, unrelated to this work. It now uses 25 days.
- **Query budget test counted other test hosts' queries.** It counts database commands process-wide, so a background poller of an isolated test host could add one to a measurement. This change adds one more isolated host, which made that likelier. Each endpoint is now measured three times and the lowest count is kept; the budgets are unchanged.
- **Intermittent rate-limit test.** One full API run also failed `IntegrationTests.A_key_is_rate_limited_per_minute` (no 429 within its loop). Under full-suite load the loop can cross a minute boundary. It passed in the next full run and was not changed.
- **Lists widening their cards.** On the home page, lists with one-line text widened their card past the page. The lists now shrink to their column.

## Verification
| Check | Result |
|---|---|
| `tsc --noEmit`, `eslint`, `next build` | pass (one pre-existing `<img>` warning in the app icon) |
| `dotnet test api/AnnabaDigital.sln` | **640 passed, 0 failed** (639 before; +1 `InsightsTests`) |
| Full Playwright suite | **225 passed, 0 failed** (223 before; +2 `home-insights.spec.ts`) |
| Every screen opened as administrator and requester (40 screens, 1440×900) | no console errors; no horizontal overflow after the list-width fix |
