# Brief: demo data modules (shared by everyone writing a module)

## Goal
The team will give a live **product demo** of this helpdesk (GA Helpdesk: Next.js UI in `src/`, .NET API in `api/`). Requirements from the product owner:
1. **Every screen has data** (no empty states, no "nothing here yet") for the person who would open it.
2. **Every flow can be walked live**: for each flow in your area, leave at least one item in the *starting state* so the presenter can do the next step on stage (approve, check in, hand over, complete, ...), plus items in every other state so lists, filters, badges and reports look alive.

The data is created **through the public API as the demo cast** (never by INSERT), so validation, audit trail, notifications and numbering behave as in real use. Backdating of timestamps through SQL (`d.sql`) is allowed and expected where the API stamps "now" but a report needs history.

## Cast (password for all: `Passw0rd!demo`; keys are what `d.as("key")` takes)
| key | person | role | notes |
|---|---|---|---|
| admin | Sarah Wijaya | super_admin | platform admin, sees everything |
| useradmin | Daniel Tan | user_admin | invitations, users |
| auditor | Maya Putri | security_auditor | audit log, security events |
| hendra | Hendra Kusuma | ga_manager | configures the helpdesk; second approval step; manager of GA |
| putri | Putri Anggraini | ga_supervisor | queue, assigns, confirms, hands out supplies, manages bookings; lead of "General Affairs" |
| agus | Agus Setiawan | ga_agent | AC/electrical technician; lead of "Facilities Engineering" |
| bayu | Bayu Nugroho | ga_agent | general/cleaning technician; lead of "Cleaning & Pantry"; manager of the warehouse |
| sinta | Sinta Maharani | receptionist | reception desk + mailroom; lead of "Reception & Mailroom" |
| joko | Joko Susilo | vendor | person of **PT Sejuk Abadi** (link him on the vendor page: vendors module) |
| dimas | Dimas Prakoso | user | Head of Operations = direct manager of rizky, anita, fajar, lina (their approvals and supply requests go to him) |
| rizky, anita, fajar, lina | employees | user | requesters (Operations, HR, Finance, Marketing) |

Setup (module `01-setup.mjs`, already written, runs first) created: sites/buildings/floors/rooms (`d.ctx.loc.*`: site, tower, lobby, reception, lvl3, garuda, merak, pantry, lvl5, server, basement, warehouse, bandung, bdgFloor), teams (`d.ctx.team.{ga,eng,clean,reception}`), calendars (`d.ctx.cal`), SLA policies, assignment rules, approval policies (Pool car booking: requester's manager; Event set-up: manager then Hendra; Desk or office move: Hendra), `d.ctx.services` (service catalog list), `d.ctx.userIds`. When your module is run alone with `--only x`, setup is run first automatically, so `d.ctx` is filled. Read `scripts/demo/01-setup.mjs` as a model of a module.

## Toolkit (`scripts/demo/lib.mjs`, read it first; do NOT edit it, put helpers in your own module and tell me what you missed)
`const d = ...` is passed to `run(d)`. `await d.as("hendra")` → signed-in client with `.get/.post/.put/.patch/.del/.upload` returning `{ok,status,data,text}`; `d.must(r, "what")` returns `r.data` or throws with the server message; `d.soft("what", fn)` swallows a failure and logs it (use for nice-to-haves, never to hide a flow that should work); `d.rows(x)`; `d.day(n)`, `d.at(dayOffset, hh, mm)` (a Date), `d.workday(n)` / `d.prevWorkday(n)` (day offsets of working days), `d.png()`, `d.pdf()` (valid sample files), `d.sql("...")` (psql against the demo DB), `d.userId("dimas")`, `d.asDevice(key, userAgent)` (a second sign-in = another session), `d.CAST`.

## Module contract
File `scripts/demo/NN-<key>.mjs`: `export const name = "<key>"; export async function run(d) { ... }`. Orchestrator: `scripts/demo-seed.mjs` (order: setup, spaces, assets, stock, servicedesk, admin, history). It must:
- succeed on a **fresh** database in one run, and when run a second time **not crash or duplicate** (look up by unique name/code and skip, or return early with a log line);
- be reasonably fast (a few minutes at most);
- log one line per area (`d.log("  bookings: 14 …")`).

## Data quality rules
- It must look like a real Indonesian company, not a test: realistic Bahasa Indonesia for what people type (ticket subjects, comments, notes, purposes, descriptions), English only where a company would use it (product names, "meeting room"). **No "[Demo]" prefixes, no lorem ipsum, no "test", no "foo".** Real-looking Indonesian names, companies (PT/CV), phone numbers (+62 8xx), plate numbers (B 1234 XYZ), rupiah amounts that make sense.
- **Dates are relative to the day of seeding**, and the seed can run at any hour or on a weekend. For live-flow items use windows relative to *now* (e.g. a visitor expected from now-30 min until now+4 h; a booking that starts in 20 min) rather than fixed clock hours. Use `d.workday(n)` for "next working days". Past-dated history comes from backdating with `d.sql`.
- Spread things: several people as actors, mixed statuses, one or two awkward cases (a late item, a rejected one, a cancelled one, a blocked one) because real lists have them, and a **realistic mix of volumes** (10 to 40 rows where a list/report needs shape, not 3).
- No real external calls: e-mail is a console sender (nothing leaves the machine). No real credentials/keys anywhere.

## What to read to learn the API
Routes: `api/src/AnnabaDigital.Api/Endpoints/*.cs` (permission per route in `.RequirePermission(...)`), request/response records and rules in `api/src/AnnabaDigital.Application/**` (and `AnnabaDigital.Domain`), entities in `AnnabaDigital.Domain/Entities`. Real request examples: `e2e/*.spec.ts` and `api/tests/AnnabaDigital.Tests/**` (grep for the endpoint). Existing lighter script with working calls for many modules: `scripts/demo-data.mjs` (copy calls from it, but this new seed is richer and not labelled "[Demo]"). Product behaviour: `docs/68-helpdesk-user-manual.md` (screens and flows by role, in Bahasa Indonesia), `docs/` reports per slice. UI pages: `src/app/(app)/**` and `src/config/nav.ts` (which menu needs which permission: check the data your module makes shows on those pages for the right person).

## Working rules
- **Isolated stack**: you have your own database and API so nothing collides with the others. Start/reset it with `node scripts/demo/stack.mjs <name> <port> --reset --no-build` (always `--no-build`; the API is already built; **never run `dotnet build`**). Run your module with `node scripts/demo-seed.mjs --api http://localhost:<port> --db annaba_demo_<name> --only <key>`. The API log is `%TEMP%/demo-api-<name>.log` (one-time links of e-mails appear there). Docker Postgres container `annaba-digital-dev-db` is already running. Stop your API when done: `node scripts/demo/stack.mjs <name> --stop`.
- **Do not touch** the C# app, the Next.js app, `lib.mjs`, `01-setup.mjs`, `demo-seed.mjs`, other people's modules, the dev servers on ports 3000/5080, or the databases `annaba_digital` and `annaba_demo`. No git commits.
- If the app has a bug or a flow cannot be made demonstrable through the API, **do not work around it silently**: note it in your report.
- Verify what you claim: after your run, read the lists/summaries back through the API as the persona who would open that screen and check the numbers (not just that calls returned 200).

## Deliverables
1. Your module file (and nothing else under `scripts/demo/` except your notes).
2. `scripts/demo/notes/<key>.md` (structure below; it feeds the demo guide, written in Bahasa Indonesia is fine, English also fine):
   - **Screens covered**: screen/route → who opens it → what data is there (counts, states).
   - **Flows ready to demo**: flow → persona → where to click / which item to start from → what should happen (state before → after). Concrete names so the presenter can find the item.
   - **Not demonstrable / bugs / caveats**: honest list.
   - **Backdating done** (what was shifted with SQL).
3. Final message to me: ≤ 12 lines: what the module creates, run time, verification you did, open problems.
