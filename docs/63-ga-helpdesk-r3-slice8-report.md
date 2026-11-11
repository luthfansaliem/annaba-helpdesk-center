# 63 — GA Helpdesk R3 slice 8: report exports and scheduled reports

Status: **implemented, verification below**. Eighth slice of R3 (`55-ga-helpdesk-r3-plan.md`). It covers RPT-007: exports and scheduled reports.

## What was done
1. **Seven reports as tables** (*Insights → Report exports*):

   | Report | Needs | What it has |
   |---|---|---|
   | Tickets | `tickets.read_team` | Every ticket raised in the period that you can see |
   | SLA attainment | `tickets.read_team` | By priority, team and service |
   | Satisfaction | `tickets.read_team` | Per week or month |
   | Costs | `cost.read` | By category, including vehicle trips |
   | Maintenance compliance | `pm.read` | By plan |
   | Room and vehicle utilisation | `bookings.read_all` | Per resource |
   | Stock on hand | `inventory.read` | Every item at every place, with its value |

2. **Download now.** Choose a report, the period (today, 7, 30, 90 or 365 days) and the file type, CSV or Excel.
   - **CSV** is UTF-8 with a byte-order mark, so Excel shows Indonesian names correctly.
   - **XLSX** is a real one-sheet workbook with a bold, frozen header row. It is written by the API itself, with no extra library.
3. **Scheduled reports.**
   - **What to send:** a name, the report, the file type and the period it covers.
   - **When:** daily, weekly on a chosen day, or monthly on day 1–28, at a chosen hour in the organisation's time zone (the default calendar's).
   - **To whom:** up to 20 people, given by e-mail address.
   - *Send now* sends it at once and leaves the regular time unchanged.
   - The list shows the next run, the last result ("Sent to 3 people; 1 skipped (no access)"), and anyone on the list who can no longer see the report.
4. **Who receives what.**
   - Every file is built **separately for each recipient, with that person's own access**. A team lead receives their team's tickets, not everyone's.
   - When saving, every recipient must be an active person in the organisation who can open the report, and so must the owner.
   - At each run, anyone who lost access or left is skipped. If the owner lost access or left, the schedule is paused.
5. **Limits and safety.**
   - At most 5 000 rows per file. A cut-off file says so, in the e-mail and in the `X-Report-Truncated` header.
   - Text that a spreadsheet would run as a formula (starting with `=`, `+`, `-` or `@`) is written with a leading apostrophe.
   - Downloads are `no-store`.
   - Attachments stay in the e-mail outbox only until the mail is sent or given up, like the body.
6. **Delivery.**
   - The e-mail outbox now carries one attachment. Resend sends it as a base64 attachment and SMTP as a MIME part; the console adapter logs its name and size.
   - A background worker checks every five minutes. Each due schedule is claimed by moving its next run first, so two API instances never send the same report twice.
7. **Audit:** `report.export` (report, format, rows, days), `report.schedule_create`, `report.schedule_update`, `report.schedule_delete`, `report.schedule_sent` (sent and skipped counts), `report.schedule_paused`.

## Decisions
- **No new permission.** Exporting is governed by the report's own permission, so anyone who can see a report can download it or schedule it to people who can see it too. People with `audit.read` see every schedule, which shows who sends what to whom.
- **Built per recipient, not copied.** Scoping by team or role cannot leak through a shared file, at the cost of building the report once per person (20 at most).
- **Attachment, not link.** The plan asks for e-mailed files. The limits (row cap, recipients who may see the report, audit) keep this acceptable. Organisations that do not want data in e-mail can use download only.
- **Hand-written XLSX.** One sheet with inline strings and numbers is a few dozen lines of Office Open XML. This avoids a large dependency and its licence review.

## Tests added
- **API, `ReportScheduleTests` (2, isolated organisations):**
  - **Exports:**
    - Requesters have no reports and get 403; unknown reports and formats get 422.
    - The CSV has the BOM and headers, the row shows place, quantity, low flag and cost, and a formula-like name is quoted and prefixed with an apostrophe.
    - The XLSX is a zip whose sheet holds the same values.
    - Every other report builds.
    - `report.export` is audited.
  - **Schedules:**
    - Validation covers the name, format, frequency, hour and recipients, with the reason when a recipient cannot see the report or does not exist. Requesters cannot schedule a report they cannot see.
    - A weekly schedule lands on Monday 07:00 Jakarta time.
    - Others neither see it nor send it.
    - *Send now* mails the workbook to both recipients and keeps the next run.
    - The worker sends once when due and not again until the next week; a suspended recipient is skipped.
    - A suspended owner pauses the schedule.
- **Playwright, `report-exports.spec.ts`:**
  - The admin downloads stock on hand as CSV.
  - They schedule it weekly on Friday to themselves and send it now ("Sent to 1 person"), then remove it.
  - The page and the schedule drawer pass the accessibility check.

## Known gaps
- **One sheet per file.** The SLA export puts its three groupings in one table with a *Group* column.
- **No PDF.** CSV and XLSX only, as in the PRD.
- **No custom columns or filters** beyond the period. Saved ticket views (R1) are not exportable on a schedule yet.

## Verification
| Check | Result |
|---|---|
| `tsc --noEmit`, `eslint` | pass |
| `next build` | pass |
| `dotnet test api/AnnabaDigital.sln` | **665 passed, 0 failed** (run together for R3-7 and R3-8) |
| Full Playwright suite | 232 passed, 1 failed. The new `supplies.spec.ts` and `report-exports.spec.ts` passed. The failure was the R3-6 `inventory.spec.ts`, whose label selectors were ambiguous; it was fixed and passes on its own (see doc 61) |
