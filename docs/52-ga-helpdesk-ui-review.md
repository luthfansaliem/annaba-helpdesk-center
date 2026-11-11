# 52 — GA Helpdesk: UI review of every screen

Status: **review done; the plan below is implemented — see `53-ga-helpdesk-ui-redesign-report.md`.** Requested by the owner after the first production deploy: the app "does not yet look like a helpdesk, has no analytics in each module, and the layout is still messy".

How it was reviewed (2026-11-11): the app ran locally (API in Development, Next dev) with `scripts/demo-data.mjs --tickets 30`. A script signed in as the demo administrator and the demo requester, opened 48 screens at 1440×900, saved full-page screenshots, and recorded layout metrics: KPI cards, tables, empty states, overflow, page height and console errors. Only tickets have demo data, so assets, work orders, vendors, maintenance, KB and costs were judged in their empty state. That is also what a new organisation sees on day one.

## Verdict
The helpdesk features exist (R1–R2), but the **frame around them is still the identity-and-access template**. The login page, the browser title, both home dashboards, the menu order and the help text all talk about users, roles and sessions. The helpdesk modules underneath are **bare list screens**: a full-width stack of filters, then a table. Only one of them has KPIs or charts (*Operations*). Layout primitives (filter bar, KPI grid, bar lists, detail page) are reused everywhere, so a handful of fixes in shared components will lift every screen at once.

## 1. It doesn't read as a helpdesk
| # | Where | What's wrong |
|---|---|---|
| 1.1 | Login | The headline is "Secure access for your whole team" and the sub-text is about role-based access and audit trail. The tab title is "Annaba Digital – Identity & Access". Nothing says *helpdesk* or *General Affairs*. |
| 1.2 | Home for staff (`/dashboard`) | The greeting says "what's happening with access and security". The KPIs are Total users, Active users, Suspended, Pending invitations, Roles and Sessions; the charts are User growth, User status, Role distribution and Login activity. Not one ticket, SLA or work-order number. |
| 1.3 | Home for requesters | Cards: Account, *Effective permissions*, Security status, Profile completion (raw keys `jobTitle`, `emailVerified`), Recent activity (raw audit codes `ticket.create`), Quick links (profile, sessions, settings). There's no *New request* button, no "my open requests", no knowledge-base search. |
| 1.4 | Menu | *Helpdesk* is one group of 11 mixed items (queue, three reports, work orders, assets, vendors, maintenance, three cost pages). *Identity & Access* (9 items), *Monitoring* and *Administration* carry the same visual weight as the daily work. Requesters also see *Identity & Access*. |
| 1.5 | Ticket page | The back link says "My requests" even for an agent who came from the queue. |

## 2. No analytics per module
Charts are used on only three screens: the IAM dashboard, *Operations* and *Users overview*. KPI cards appear on Operations, SLA, Satisfaction, Maintenance compliance and Cost report, and nowhere else.

| Module | Has today | Missing (what a GA manager asks) |
|---|---|---|
| Ticket queue | table | counts per status as tabs, unassigned, due today, SLA at risk, my open |
| Work orders | table | open / overdue / done this month, in-house vs vendor, average time to complete, trend, cost of work this month |
| Assets | table | total and by status, by type, by place, warranty expiring (30/60/90 days), assets with most tickets, maintenance due |
| Vendors | table | active contracts, contracts expiring, spend per vendor, rating and on-time ranking |
| Maintenance | table (+ separate compliance page with KPIs only) | compliance trend, due this week, overdue, completed per month, a calendar view |
| Knowledge base | list | published articles, views, helpful %, requests avoided (deflection), top articles |
| Cost report | 3 KPIs + table | monthly trend chart, split by category / place / cost centre, budget burn per budget |
| Budgets | table | % used per budget as bars, the ones over 80% at the top |
| Satisfaction | 4 KPIs + tables | trend over time, 1–5 distribution, by team / agent / category |
| SLA report | KPIs + tables | attainment trend, breaches by category and team |

## 3. Layout and visual issues
| # | Where | Issue |
|---|---|---|
| 3.1 | Every list page (filter bar) | Each filter is a full-width field stacked under the last one. The ticket queue spends about 470 px on nine filters before the first row; Cost report, Work orders and KB do the same with 1–3 fields. They should sit in one row with a *More filters* button and chips. |
| 3.2 | Ticket queue table | Ticket numbers wrap onto two lines (`GA-2026-` / `000030`); status pills wrap ("In / progress"); SLA cell crams state + due date. |
| 3.3 | Ticket detail | The conversation sits in the narrow left column of a 3-column card grid, the right two-thirds is empty below the fold, SLA is at the bottom. Action buttons touch each other; the upload is the raw browser file input; "Send comment" and the internal-note checkbox wrap together. A helpdesk ticket wants a wide conversation column and a fixed side panel (status, assignee, SLA timer, place, asset, work orders). |
| 3.4 | Operations | 10 KPI cards in a 3-column grid leave an orphan card. The line chart's curve dips **below zero** (spline overshoot) and the last x-label is cut ("11-1"). Bar-list labels and numbers run together ("General Affairs24", "Medium18"). The *Help* button sits on top of the *Period* control. |
| 3.5 | New request | The catalog is one long single-column text list (about 1,700 px). There are no icons or tiles and no "popular" section; the width is wasted. |
| 3.6 | Empty states | "No work orders here." / "No articles found." in a bare table row. They don't explain the module or offer the first action (*Add asset*, *Write first article*, *Create plan*). |
| 3.7 | KPI formatting | The currency shows on one card only ("0.00 IDR", then "0.00" and "0.00"). Rupiah should be `Rp 0`, without decimals. |
| 3.8 | Sidebar | The organisation chip is clipped for the administrator ("Default organisation" cut in half). |
| 3.9 | All pages | A React hydration mismatch is logged on every page (`A tree hydrated but some attributes of the server rendered HTML didn't match`). It is invisible to users but means server and client render differently (likely theme/locale attributes) and must be fixed. |

## Recommended plan
Shared components first, so every module benefits:

1. **Helpdesk identity and navigation.**
   - Product name "Annaba Helpdesk" on login, title and manifest; login copy about requests and service.
   - Menu by job:
     - *Home*
     - *Service desk*: Queue, Work orders, Approvals
     - *Assets & maintenance*: Assets, Maintenance
     - *Vendors*
     - *Knowledge base*
     - *Insights*: Operations, SLA, Satisfaction, Costs, Budgets
     - *Settings*: helpdesk setup, people and access, administration, collapsed
   - Requesters see only Home, New request, My requests, Knowledge base and Notifications.
2. **Role-based home pages.**
   - *Requester*: a large "What do you need?" search over the catalog and KB, popular services as tiles, my open requests with status and SLA, and pending ratings.
   - *Agent*: my queue counts, due today, at risk, my work orders, and recent activity on my tickets.
   - *Manager*: the Operations analytics as the home page.
   - The IAM dashboard moves to *Settings → People & access*.
3. **One page pattern for every module.**
   - Header with the primary action.
   - KPI strip of 4–5 cards that never leaves an orphan.
   - 1–2 charts.
   - A compact one-row filter bar with chips and *More filters*.
   - The table.
   - An empty state with an explanation and the first action.
4. **Module analytics** (table in §2). Each module needs a small `GET /api/<module>/summary` endpoint (counts and series, permission-scoped like the list) and the existing chart components. Chart fixes come first: monotone curve without overshoot, label spacing, axis labels.
5. **Ticket detail redesign.** Two columns: conversation and activity on the left, properties, SLA timer and work orders on the right. Add a drop-zone upload and a proper button group.
6. **Polish.** Catalog tiles with category icons, Rupiah formatting, no-wrap ticket numbers and pills, fix the hydration mismatch and the clipped organisation chip.
7. **Richer demo data.** Extend `scripts/demo-data.mjs` to create assets, vendors and contracts, work orders with costs, PM plans, KB articles and ratings, so every module and chart can be judged and demoed.

Estimated size: 1–2 is about a day; 3, 5 and 6 together are about two days; 4 is the largest, at about half a day per module including API, tests and e2e; 7 is about half a day. Every step keeps the existing API and Playwright suites green and adds e2e checks for the new home pages and summaries.
