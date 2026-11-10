# 25 — GA Helpdesk, R1 slice 2: service catalog and dynamic forms

Status: **implemented and verified** (backend against PostgreSQL, browser flows and accessibility in Chromium). Part of release R1 (see `23-ga-helpdesk-prd.md`). Requirements covered: **CAT-001** (categories + services), **CAT-002** (starter catalog), **CAT-003** (dynamic form builder, versioned; file/asset pickers deferred), **CAT-004** (requester portal — read side only; the "my requests" and submit side arrive with tickets).

## What was built
| Area | Where |
|---|---|
| Entities `ServiceCategory`, `ServiceItem`, `ServiceFormVersion` | `api/src/AnnabaDigital.Domain/Entities/Catalog.cs` |
| **Form engine** (pure): validates a form *definition*, evaluates *answers* (types, required, limits, patterns, conditional visibility) | `api/src/AnnabaDigital.Domain/Forms/FormEngine.cs` |
| Services `CatalogService` (categories + services), `ServiceFormService` (versions), `PortalCatalogService` (requester view + answer check) | `api/src/AnnabaDigital.Application/Helpdesk/` |
| Starter GA catalog (8 categories, 15 services, every one with a real form), installed for every organisation | `CatalogTemplates.cs`, called from `HelpdeskDefaults.SeedAsync` (new and existing organisations; idempotent) |
| Endpoints `/api/catalog/*` (admin, one permission per route) and `/api/portal/*` (any signed-in person) | `api/src/AnnabaDigital.Api/Endpoints/CatalogEndpoints.cs` |
| Tables `service_categories`, `services`, `service_form_versions`; migration `Catalog` | `AnnabaDigitalDbContext.cs`, `Persistence/Migrations/20261110001836_Catalog.cs` |
| Permissions `catalog.read`, `catalog.manage`, `forms.manage` (Super Admin gets them automatically) | `Permissions.cs` |
| UI: Helpdesk Setup → **Service Catalog** (services/categories) and the **form builder** with live preview, version history and "load into draft" | `src/app/(app)/setup/catalog/`, `src/components/forms/dynamic-form.tsx`, `src/lib/forms.ts` |

## Rules enforced (each has a test)
- **Categories:** two levels at most; names unique among live siblings (two partial unique indexes, because a NULL parent never collides in one); a category with sub-categories cannot move under another; cannot be archived while a live sub-category or service is inside; restoring needs a live parent and a free name.
- **Services:** start unpublished with no form; category must be live, owning team must exist and be live; name unique per category among live services; archiving also unpublishes, so restoring never silently re-publishes; a team that live services use cannot be archived; restoring needs a live category, a live team and a free name.
- **Forms are versioned and immutable.** Saving a changed form appends version N+1 and points the service at it; saving an identical form is a no-op (no empty versions); version numbers are unique per service in the database, so two concurrent saves cannot both claim one. Old versions stay readable exactly as saved — this is what lets tickets keep the version they were created with. Saving from a stale copy of the service returns 409.
- **A broken form can never be saved.** Server-side validation of keys (unique, `^[a-z][a-z0-9_]{0,39}$`), types, choices (1–50, distinct), numeric limits, text limits, regex patterns (compiled with a timeout), and conditions (may only look at an *earlier* question — which rules out cycles — and only at values that question can hold). Errors come back per question (`fields[2].options`); the UI names the question.
- **Answers** (`POST /api/portal/services/{id}/check`, the one place answers are validated; ticket creation will call it): required rules apply only to *visible* questions; answers to hidden questions are dropped; unknown keys are refused; numbers must be JSON numbers; dates are `yyyy-MM-dd`; date-times must carry a zone and are normalised to UTC; choices must be listed options; location/person answers must be an *active* location/person of the same organisation.
- **Visibility in the portal:** only published, non-archived services in a fully live category chain; `staff` services only for people holding `catalog.read`; a hidden service answers 404, the same as a missing one. Empty categories are not shown. Search is case-insensitive and treats `%`/`_` literally.
- **Everywhere:** server-side permission per admin route (`forms.manage` is separate from `catalog.manage`), tenant isolation (cross-organisation ids → 404, cross-organisation category/team references are rejected), optimistic concurrency through `version`, and an audit row (with diff) for every change.

## Verification
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16 via Testcontainers) | **402 passed, 0 failed** — includes 20 pure form-engine tests (theory cases counted), 2 starter-template tests (every template form passes the admin validator), 12 catalog integration tests |
| `tsc --noEmit`, `eslint` on new files, `next build` | pass |
| Playwright `catalog.spec.ts` (Chromium): starter catalog visible, create draft → build form → publish, conditional preview, new version / unchanged form can't be saved / load old version, duplicate key caught, builder + drawer axe | **6 passed** |
| Playwright axe (WCAG 2.1 A/AA, serious+critical), light **and** dark, for `/setup/locations`, `/setup/teams`, `/setup/calendars`, `/setup/catalog` (first browser coverage of the slice-1 pages too) | **8 passed** |
| Full Playwright suite (existing + new specs, one shared database) | **114 passed, 0 failed** |
| Frontend `vitest` | still cannot run on Node 20.16 (vitest 5 needs ≥ 22.12) |

## Decisions and known gaps
- **Staff audience** is defined as "holds `catalog.read`" until the GA system roles (Agent, Supervisor, …) exist; then it becomes "holds an agent role".
- The preview's visibility logic (`src/lib/forms.ts`) mirrors the server's so the admin sees real behaviour; **the server is the authority** and re-evaluates on every submit. It has no unit test yet (vitest is blocked); it is exercised by the e2e preview test.
- **Not in this slice:** file/photo and asset-picker questions (need attachments and the asset register), SLA policy link on a service (arrives with SLA clocks), visibility by role/department beyond everyone/staff, the requester-facing screens (browse, fill, submit, "my requests" — they need the ticket endpoint to submit to), icon picker (icons are free-text keys today), template content is English only.
- The starter catalog is installed for existing organisations the next time the API starts (idempotent: only when the organisation has no categories at all).

## Next
Ticket lifecycle: create from a service (using `CheckAnswersAsync` and recording the form version), statuses, comments, attachments, "my requests", then assignment rules → SLA clocks/escalation → approvals → notifications → dashboards.
