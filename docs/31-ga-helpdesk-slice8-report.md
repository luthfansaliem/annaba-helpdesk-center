# 31 — GA Helpdesk, R1 slice 8: go-live checklist, scanner and production gates, role review, demo data

Status: **implemented and verified** (API against PostgreSQL, browser flows and accessibility in Chromium; the demo script run against a live API). First hardening slice of release R1 (see `23-ga-helpdesk-prd.md` §14). Requirements covered: **ADM-001** (onboarding), the malware-scan hook of **TKT-005** made real, and the role review for the GA seed roles.

## What was built
| Area | Where |
|---|---|
| `SetupService`: the go-live checklist (`GET /api/setup/status`) and the **quick start** (`POST /api/setup/quick-start`) | `api/src/AnnabaDigital.Application/Helpdesk/SetupService.cs`, `Api/Endpoints/SetupEndpoints.cs` |
| UI: Helpdesk Setup → **Get started** (`/setup/start`): progress bar, quick-start form, checklist with a link to the screen behind every step | `src/app/(app)/setup/start/page.tsx`, `src/config/nav.ts` |
| `ClamAvAttachmentScanner`: scans every upload through a ClamAV daemon (INSTREAM over TCP), **fails closed** | `Infrastructure/Storage.cs`, registered when `Storage:ClamAvHost` is set |
| `IAttachmentScanner.Active`: lets the checklist say whether anything really scans | `Application/Abstractions/Abstractions.cs` |
| **Production start-up gates** for files: absolute `Storage:Path`, and either `Storage:ClamAvHost` or an explicit `Storage:AllowUnscanned=true` | `Api/StartupChecks.cs` |
| Deploy: persistent `annaba-digital-files` volume, storage variables, optional `clamav` service (commented), image creates `/data/files` for the non-root user | `deploy/docker-compose.coolify.yml`, `deploy/env.example`, `api/Dockerfile` |
| GA Manager role review (below) | `Domain/Security/Permissions.cs` (`SystemRoles`) |
| `scripts/demo-data.mjs`: realistic demo data through the public API | `scripts/demo-data.mjs` |
| Runbook section for attachments, scanner, backups of files, workers, go-live, demo data | `22-staging-runbook.md` |

## The checklist and the quick start
- **Required steps (6)**: a site, a business calendar, a team with at least one person, a published service, a live SLA policy, a live assignment rule. A new organisation already starts with a calendar, the published starter catalog and four starter SLA policies, so it opens at 3/6; the remaining three are exactly what the quick start does.
- **Advice only**: approval policies, a real virus scanner, and "a ticket exists" (send a test request).
- **Quick start** creates, in **one transaction** and only where nothing exists: the first site (code derived from its name), the first team with the chosen people (and optional lead; an existing but empty team is filled instead of a second one created), the four SLA policies if none are live (same targets as the starter ones), and an **"All requests" → team** rule with the *least loaded* strategy. It reuses the ordinary services, so validation, uniqueness and audit entries are those of the setup screens. Run it again: nothing is created twice.
- **Permissions**: the checklist is visible to anyone who configures any part of the helpdesk; the quick start needs **all four** of `locations.manage`, `teams.manage`, `sla.manage`, `rules.manage` and names the missing one (`403`) — it cannot be used to get around a permission.
- Who can be chosen: people who can work tickets (the same list as "assign a ticket"). A brand-new organisation has only its administrator until colleagues are invited and given the *GA Agent* role; the page says so.

## Role review (GA seed roles)
| Role | Finding | Change |
|---|---|---|
| GA Agent, GA Supervisor | Work tickets, see their scope, no configuration. Correct. | none |
| **GA Manager** | Could read locations and calendars and manage SLA, rules and approvals, but could **not** create a team, change the catalog or a form, or edit locations/calendars — the person who owns the helpdesk depended on a Super Admin for day-to-day setup. | Now also holds `locations.manage`, `calendars.manage`, `teams.manage`, `catalog.manage`, `forms.manage` (with the matching read permissions). Still **no** `users.*`, `roles.*`, `invitations.*` or audit permission. |
| User Admin, Security Auditor, User | Not helpdesk roles; no ticket, SLA or rule permission; the auditor only reads. Correct. | none |

A unit test pins these relationships (the manager is a superset of the supervisor, who is a superset of the agent; nobody but Super Admin holds everything; the manager holds no identity permission; whoever can change a screen can also open it). **Upgrade note:** existing organisations keep their copy of the role (only the protected Super Admin role is re-synced), so on an existing tenant add the permissions to *GA Manager* in Roles; new organisations get them automatically.

## Production gates for files
`StartupChecks` already refused insecure secrets and mail settings. It now also refuses to start in Production when attachment storage is a relative path (lost with the container) or when no virus scanner is configured and nobody has written down that this is intended. This turns "remember to connect an antivirus" into a deployment failure that names the variable to set. The compose file wires `CLAMAV_HOST` and `ALLOW_UNSCANNED_UPLOADS`; **an existing staging deployment must set one of them before its next release.**

## Verification
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16, Testcontainers) | **557 passed, 0 failed** |
| `SetupTests` (6): fresh organisation opens at 3/6 with the right steps required/advisory; only configuring roles see the checklist and a person with three of the four permissions is refused naming the fourth; every validation message and "nothing half-made after a refusal"; a full quick start (site, staffed team with lead, four policies, rule → the new request would get the *Standard* policy and land with the team, audit entries under the administrator) and a second run that creates nothing; an existing empty team is filled not duplicated; archived policies are recreated and a second organisation is untouched; system-role invariants | pass |
| `ClamAvScannerTests` (3), against a fake clamd: a 200 KB file streamed in chunks and reassembled byte for byte, `zINSTREAM` command, clean and `FOUND` verdicts; refused when the daemon is down, answers with an error, or never answers (timeout); only the no-op scanner reports inactive | pass |
| Production start-up checks (5 new cases): absolute path + scanner, or explicit no-scan, accepted; relative path, no scanner, blank host refused | pass |
| `tsc --noEmit`, `eslint`, `next build` | pass (one existing `<img>` warning) |
| Playwright `setup.spec.ts` (3): checklist and links, axe; a **new organisation** is created, its administrator accepts the invitation, sees "3 steps left", gets the field errors, runs the quick start and then sees "ready to take requests" with the created site, team and rule on their own screens, axe at each state; a member without setup rights is turned away | pass |
| Full Playwright suite incl. axe light + dark for the new page (`quality.spec.ts`) | **152 tests: 151 passed in the full run; the 1 failure was a test race (below) and passes after the fix, together with the whole admin and setup specs rerun from a cold stack (23 passed)** |
| `scripts/demo-data.mjs` against a live API on an empty database | setup completed, 10 requests created (2 assigned, 4 in progress, 2 on hold, 2 resolved); second run created nothing; refused a non-local host without `--yes` |
| `docker compose config` on the deploy file | valid, storage variables and volume resolved |

Findings on the way: the "add user" end-to-end test clicked *Send invitation* before the role list had loaded, so the default *User* role was not yet ticked and the form said "Select at least one role" — a race that only showed on a cold API; the test now waits for the role to be ticked.  the first draft of the GA Manager change *replaced* the read permissions with the manage ones, which would have hidden the Locations and Calendars screens from the manager (the existing role test caught it); a new organisation already had starter SLA policies, so the quick start's SLA step is a safety net rather than the usual path.

## Decisions and known gaps
- **Not rehearsed**: the ClamAV container on Coolify, a restore of the files volume together with a database dump, and the Dockerfile change (`/data/files` owned by the app user) in a built image — the compose file validates, the image was not rebuilt here.
- The checklist does not check e-mail delivery, SSO or backups; those are covered by the runbook and the existing start-up checks.
- Pilot-tenant items from the PRD §14 checklist are not part of this slice: UAT with a real GA team, native-speaker review of the Indonesian copy, load test against the §10 budgets, and the external penetration test.
- Demo data is a developer tool, not a tenant feature: no "load sample data" button exists in the product.

## Next
Either continue the R1 exit checklist (privacy and retention policy for ticket data and files, a load test with realistic volumes, notification templates per tenant, in-app help for the three personas), or start release R2 (work orders, assets, preventive maintenance).
