# 46 — GA Helpdesk, R2 slice 6: satisfaction (CSAT)

Status: **implemented and verified** (API and Playwright suites on the final code; see the table at the end). Sixth slice of release R2 (`40-ga-helpdesk-r2-plan.md`): **TKT-009** (1–5 rating and comment after resolution, low scores raise an alert, reportable per agent, team, site and vendor), **RPT-006** (CSAT report: own data for agents, everything for supervisors) and the CSAT part of **VND-004** (vendor scorecard).

## What was done
1. **Rating a request.** When a request is resolved, its requester sees *How did it go?* on the request page: 1–5 stars (named *Very poor* … *Excellent*) and an optional comment (*What went wrong?* for 1–2). They can change it while rating is open — 14 days after resolution — and see *Your rating* afterwards. The *resolved* e-mail now asks for a rating. Each rating or change is a visible line on the timeline.
2. **Low-score alert.** A score of 1 or 2 notifies the leads of the ticket's team (in the app and by e-mail, kind `ticket.low_rating`, switchable), or — when the team has no lead — the people who see every ticket (at most 20). Once per ticket: lowering it again does not alert twice.
3. **Satisfaction report** (*Reports → Satisfaction*): average rating, share satisfied (4–5), number of low ratings, the spread of 1–5, a table by team, agent, site or vendor, and the latest 20 comments linked to their tickets. Supervisors and managers (`tickets.read_all`) see everything; agents see only the ratings of requests they worked on.
4. **Vendor scorecard**: the average requester rating of tickets that had work by the vendor, and how many ratings it is based on.
5. Staff see the requester's rating on the ticket.

## Rules worth knowing
| Rule | Why |
|---|---|
| Only the requester rates, only after resolution, for 14 days. | The person who asked judges the result; old requests are not re-judged. |
| The person, team and place working the ticket **when it was rated** are kept with the rating. | Reassigning a ticket later must not move the credit or the blame. |
| Planned-maintenance tickets are never rated. | Nobody asked for them; their owner rating them would skew the report. |
| Agents see only their own ratings; `tickets.read_all` sees all. | PRD RPT-006: own data for agents, team data for supervisors. |
| By vendor, a ticket counts for each vendor that did work on it; tickets without a vendor are "In-house". | A ticket can involve more than one vendor. |
| The low-score alert goes once per ticket and never to the requester. | Avoid alert storms when someone adjusts their rating. |

## Tests added
**API — `RatingTests` (3):** not rateable before resolution; the requester can rate, staff cannot (not even the assignee), validation of score and comment length, a 2 alerts the team lead (in-app and by e-mail) but not the other members, changing to 1 does not alert again, the change is on the timeline and there is still one rating; the report by team (count, average, satisfied %), the latest comments, an agent's own-only report with the 1–5 spread, a lead who worked nothing sees nothing, by vendor, the vendor scorecard rating, requesters refused, an unknown grouping refused; in an isolated organisation, rating refused 15 days after resolution, and a planned-maintenance ticket resolved by its work order can't be rated by its owner.
**Playwright — `csat.spec.ts` (1) and `/reports/csat` in the accessibility sweep (light and dark):** the requester sees *How did it go?*, sending without stars asks for them, rates 2 with a comment, changes it to 4 (timeline line shown); the admin sees the requester's rating on the ticket and the ticket in the report's comments; requesters get `403` for the report; the rating box and the report pass the accessibility check.

## Decisions and known gaps
- **No survey by e-mail link** (rating without signing in): the e-mail sends people to the request page. A signed one-click link can come with R2-9 (e-mail to ticket).
- **Threshold and window are fixed** (1–2 is low, 14 days); per-organisation settings were not needed yet.
- **Team-level view for supervisors who lack `tickets.read_all`** is not a separate scope: in the seeded roles supervisors have it; a custom role with only `tickets.read_team` sees its own ratings.
- No new permission; existing organisations need nothing beyond the migration (runbook).

## Verification of the functional behaviour
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16, Testcontainers) | **627 passed, 0 failed** (624 before; +3 `RatingTests`) |
| `tsc --noEmit`, `eslint`, `next build` | pass (one pre-existing `<img>` warning in the app icon) |
| Full Playwright suite (light + dark accessibility, responsive) | **206 passed, 0 failed** (203 before; +1 satisfaction, +2 accessibility pages) |

## Next
R2-7: knowledge base — articles with versions, publishing, search, and suggestions while making a request.
