# 38 — GA Helpdesk, R1 slice 15: in-app help for the three kinds of people

Status: **implemented and verified** (Playwright suite on the final code; the backend did not change in this slice, so the API suite of slice 14 still stands). Part of release R1 (`23-ga-helpdesk-prd.md`, §14 pilot checklist): "in-app help for the three personas".

## What was done
1. **A Help page** (`/help`, in the sidebar under *Personal*, open to every signed-in person) with 20 topics in both English and Bahasa Indonesia, grouped by who is reading:
   - **I need something** (6): make a request; follow your requests; confirm, reopen or cancel; when your request needs approval; notifications and e-mail; your data and privacy.
   - **I work the tickets** (6): the ticket queue (filters and saved views); work a ticket; change many tickets at once; deadlines (service levels); approving and being away; reports.
   - **I set up the helpdesk** (8): going live (checklist and quick start); places, teams and working hours; the service catalog and forms; deadlines and who gets new tickets; approval policies; people and roles; how long data is kept and legal holds; files, backups and restoring.
2. **It opens on the right tab:** administrators land on *set up*, people who work the queue on *work*, everyone else on *need something*; the three buttons switch.
3. **Search** looks through every topic (title, summary, steps, tips) in the current language and shows which kind of person each result is for.
4. **A Help button on the main screens** (Ticket Queue, New request, My requests, Get started, Approvals, Operations, Data Retention) opens the matching topic directly (`/help?topic=<id>`), scrolled into view and on the right tab. `PageHeader` has a `help` prop for any other screen.
5. Every topic can point to the screen it is about with a button.

## How it is built
- The text lives in **`src/content/help.ts`**, in both languages side by side, next to the facts it describes, rather than in the UI dictionary: a topic is read as a whole, so it must not be half-translated. Names of buttons and menus are written as they appear on screen in each language.
- `**bold**` in the text marks a screen label; nothing else is interpreted, so content can never inject markup.
- The facts in the text were checked against the code: reopen window 7 days and auto-close after 3 days, 8 MB and 20 files, at most 100 tickets in a bulk change, 14-day account closure wait, first-matching-policy rule for SLA, the form question types, and the button names.

## Found on the way
- **The sidebar's collapse button had a fixed English accessible name** ("Collapse menu") while its visible label follows the language, so in Indonesian a screen reader heard a different name from what is on screen (accessibility rule *label in name*, found by the new test's axe pass in Indonesian). Fixed: the accessible name now follows the language.

## Tests added
**Playwright — `help.spec.ts` (3) and `/help` in the accessibility sweep (light and dark):** a requester starts on the right tab, switches tabs, searches (and sees "nothing matches"); a topic link opens its tab; the sidebar leads to the help; the whole page works in Indonesian (switching through *Settings → Language*, restored afterwards) with an accessibility pass in Indonesian; an administrator starts on *set up*, the Help button on the queue opens its topic, and **every screen any topic points to loads** (a dead-link check that fails the day a screen is renamed or removed).

## Decisions and known gaps
- **Static content, shipped with the app.** No editing in the browser and no per-organisation wording; an organisation that wants its own rules (for example its own request etiquette) cannot add them yet. This is deliberate for the pilot: the text then always matches the version of the product.
- **Not a full manual.** It teaches the everyday paths and the rules people trip over; reference detail stays in the docs (RBAC, API, runbook).
- **No screenshots or videos** (they would go stale with every UI change); steps name the buttons instead.
- **The text must be maintained with the screens.** The dead-link test catches removed screens, not changed behaviour: when a rule changes (for example the reopen window), change its topic in `help.ts` in the same change.
- **Not reviewed by real requesters or agents yet.** The wording is written from the product's behaviour; the pilot's first week should collect what people still ask the administrator, and the topics should follow.
- **The Indonesian text** was written together with the English by the same author and uses the product's own Indonesian labels; a native-speaker review is still recommended before go-live.

## Verification of the functional behaviour
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` | unchanged since slice 14: 589 passed |
| `tsc --noEmit`, `eslint`, `next build` | pass (one pre-existing `<img>` warning in the app icon) |
| Full Playwright suite (light + dark accessibility, responsive) | **172 passed, 0 failed** (167 before; +3 help, +2 accessibility pages) |

## Next
Remaining pilot-checklist item: per-tenant e-mail wording (NTF-002); or release R2 (work orders, assets, preventive maintenance).
