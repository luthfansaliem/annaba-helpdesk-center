# 24 — GA Helpdesk, R1 slice 1: locations, teams, business calendars

Status: **implemented; backend verified against PostgreSQL, frontend unit/e2e not yet run.** Part of release R1 (see `23-ga-helpdesk-prd.md`). Requirements covered: LOC-001/002 (partly), SLA-003 (calendars + engine), ADM-001 (default calendar on every new organisation), ADM-003 (teams, without shifts/skills).

## What was built
| Area | Where |
|---|---|
| Domain entities `Location`, `BusinessCalendar`, `CalendarPeriod`, `CalendarHoliday`, `Team`, `TeamMember` | `api/src/AnnabaDigital.Domain/Entities/Facilities.cs` |
| **Working-time engine** `WorkingCalendar` (pure: working time between instants, add working time, next opening; time zones, DST, holidays, breaks, 24/7) | `api/src/AnnabaDigital.Domain/WorkingCalendar.cs` |
| Services `LocationService`, `CalendarService` (+ `HelpdeskDefaults`), `TeamService` | `api/src/AnnabaDigital.Application/Helpdesk/` |
| Endpoints `/api/locations`, `/api/calendars`, `/api/teams` (one permission each) | `api/src/AnnabaDigital.Api/Endpoints/HelpdeskSetupEndpoints.cs` |
| Tables, tenant filters, indexes, check constraints; migration `HelpdeskSetup` | `AnnabaDigitalDbContext.cs`, `Persistence/Migrations/20261109190152_HelpdeskSetup.cs` |
| Permissions `locations.*`, `teams.*`, `calendars.*` (granted to Super Admin automatically) | `Permissions.cs` |
| Default "Office hours" calendar (Mon–Fri 08:00–17:00, Asia/Jakarta) for every tenant, existing and new | `Seeder.cs`, `TenantService.cs` |
| UI: Helpdesk Setup → Locations, Teams, Business Calendars (EN + ID) | `src/app/(app)/setup/*`, `src/config/nav.ts`, `src/i18n/id.ts` |

## Rules enforced (each has a test in `HelpdeskSetupTests.cs`)
- Location tree: only region/site at the top; a child is always deeper than its parent (this also rules out cycles); changing type/parent must keep live children valid; codes are upper-cased, unique among live places (a partial unique index, so an archived code is reusable); a place with live children cannot be archived; a child cannot be restored under an archived parent.
- Calendars: at least one window unless 24/7; no overlapping windows; unique valid holiday dates; valid IANA zone; exactly one default per organisation (partial unique index); the default and calendars used by live locations cannot be archived; windows/holidays are replaced as a whole.
- Teams: unique live names; members must be active people of the same organisation; the member list is all-or-nothing; archived teams are frozen.
- Everything: server-side permission per route (deny-by-default coverage test still applies), tenant isolation (global filter + tenant FK; cross-tenant ids return 404, cross-tenant parents/calendars are rejected), optimistic concurrency through `version`, and an audit row (with diff) for every change.

## Verification status
| Check | Result |
|---|---|
| `dotnet build api/AnnabaDigital.sln` | passes, 0 errors |
| 17 unit tests for `WorkingCalendar` (no database) | **pass** |
| Frontend `tsc --noEmit` and `eslint` on the new files | pass |
| Migration SQL reviewed (check constraints, filtered unique indexes) | looks as designed |
| **12 integration tests in `HelpdeskSetupTests.cs`** + the existing suite (368 tests, PostgreSQL 16 via Testcontainers) | **pass** (367/368 in the last full run; the 1 failure, `AccountClosureTests.A_declined_request_leaves_the_account_alone…`, is an existing test that passed 3/3 when re-run alone, so it looks timing-sensitive under full load — not investigated further) |

Bug found by the integration run (fixed): updating a calendar added the new windows/holidays through the tracked parent's collection; their client-generated keys made EF treat them as existing rows (an `UPDATE` of 0 rows → a false "changed by someone else" conflict). They are now added through the `DbSet`. A test-only case bug (location codes are stored upper-case) was also fixed.
| Playwright axe (WCAG 2.1 A/AA, light + dark) on `/setup/locations`, `/setup/teams`, `/setup/calendars` | **pass** (added in slice 2) |
| Frontend `vitest` | **cannot run** on Node 20.16: vitest 5 needs Node ≥ 22.12 |

## How to run the missing checks
```bash
# Option A (default): Docker running → Testcontainers starts PostgreSQL
npm run api:test

# Option B: no Docker — point the tests at any PostgreSQL 16 where the user may CREATE DATABASE
ANNABA_TEST_PG="Host=localhost;Port=5432;Username=postgres;Password=…" npm run api:test

# Frontend unit tests need Node 22.12+ (or 24)
npm test
```
`PostgresFixture` honours `ANNABA_TEST_PG` (added in this slice); each run creates its own database, so runs never see each other's rows.

## Known gaps / next
- Teams have no skills, shifts or location coverage yet (arrive with assignment rules).
- Location `managerUserId` is stored but the UI does not pick a manager yet.
- The role matrix does not give any non-admin role the new permissions; the GA system roles (Agent, Supervisor, Manager, Requester…) arrive with the catalog/ticket slices.
- Next slices: service catalog + dynamic forms → ticket lifecycle → assignment rules → SLA clocks/escalation → approvals → notifications → dashboards.
