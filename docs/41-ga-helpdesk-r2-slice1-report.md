# 41 — GA Helpdesk, R2 slice 1: asset register

Status: **implemented and verified** (API and Playwright suites on the final code; see the table at the end). First slice of release R2 (`40-ga-helpdesk-r2-plan.md`): **AST-001** (register), **AST-004** (lifecycle trail), **AST-006** (CSV import/export with a validation report), the history part of **AST-003**, and the asset field of **TKT-001**.

## What was done
1. **Asset types** (*Helpdesk Setup → Asset Types*): a name, a description and the extra questions every asset of that type answers (for example capacity in BTU, refrigerant, inverter yes/no). The questions use the same form engine and editor as service forms, limited to text, long text, number, date, a choice and yes/no; the editor was moved into a shared component (`src/components/forms/field-drawer.tsx`) and the catalog's form builder now uses it too. Archiving a type stops it being offered for new assets; its assets keep it.
2. **The register** (*Helpdesk → Assets*): tag, name, type, serial, status (in use, in repair, stored, retired), place, the person who has it, department, purchase date and cost, warranty end, vendor name, notes, and the type's own answers. Without a tag the register hands out the next `AST-000001`; tags are unique per organisation and stored in capitals. Filters: text (literal, in tag, name or serial), type, status, place **including the places below it**, person, and "warranty ends within 30 / 90 days".
3. **The trail** (AST-004): every addition, move ("Moved from Head Office to Warehouse."), hand-over ("Handed to …" / "Taken back from …"), status change, other edit ("Changed: vendor."), retirement (with its required reason) and restore is a line on the asset's history and an audit row.
4. **Requests about an asset**: staff who may edit a ticket choose its asset in the ticket's *Edit* (search by tag, name or serial); the ticket shows it to everyone who can see the ticket; the asset lists its requests — **only those the reader may see in the queue**, so an agent does not learn about another team's tickets through the register. A retired asset cannot be chosen.
5. **CSV export and import** (AST-006): export uses the current filters and, with one type chosen, adds that type's questions as `x_<key>` columns, so the file can be edited and imported again. Import checks every line like the form does and reports problems per line; **nothing is written unless every line is correct**, and *Check the file* is a dry run. A line whose tag exists updates that asset, where an empty cell keeps the current value.

## Rules worth knowing
| Rule | Why |
|---|---|
| Retiring needs a reason and is the only way to *Retired*; a retired asset cannot be edited until restored (it comes back as *stored*). | Retirement is a decision someone must stand behind. |
| Optimistic concurrency on assets and types (`409` on a stale version). | Two people editing the same asset never overwrite each other silently. |
| Import: dates as `yyyy-mm-dd` only, amounts with a dot and no thousands separators, places by their **code**, people by **e-mail**, types by name. | No ambiguous day/month orders or "1.000"; codes and e-mails are unique where names are not. |
| Import is all-or-nothing in one transaction, at most 5,000 lines / 2 MB; export at most 20,000 lines (the response says when it was cut). | A half-applied file is worse than none. |
| Export cells that a spreadsheet would run as a formula are neutralised (`'=…`), as for the audit export. | CSV injection. |
| Permissions: `assets.read` (GA Agent, GA Supervisor), `assets.manage` and `assets.import` (GA Manager). The ticket's asset picker needs only `tickets.update`. | Agents need to see the equipment they repair; only managers change the register. |

## Tests added
**API — `AssetTests` (9):** who may read, change, import and pick; asset types (simple questions only, unique names ignoring case, archive/restore); automatic and own tags, answers checked (`fields.<key>`), warranty before purchase, retired status refused, the trail of a move + hand-over + status + detail change, stale version `409`, audit; retire/restore and the default "in service" list; every filter including place-with-children and literal search; linking a request (timeline line, shown to the requester), the asset's request list respecting queue visibility, a retired asset refused and not offered; export columns, type columns and formula defusing; import problems per line with nothing written, dry run, then creates and updates in one go with kept values; RFC 4180 CSV reading.
**Playwright — `assets.spec.ts` (3) and three pages in the accessibility sweep (light and dark):** a manager defines a type with a required question, adds an asset (automatic tag), moves it and puts it in repair (both on its history), links a request from the ticket's *Edit* and sees it on the asset, retires (reason required) and restores; a CSV file with problems is reported per line and cannot be imported, a correct one is checked then imported, and the register exports; a requester does not see the register.

## Decisions and known gaps
- **Vendor is free text** until the vendor register (R2-4); **depreciation** is not shown (informational only in the PRD).
- **No QR labels or "report a problem" from an asset yet**, no warranty reminders and no failure counts — that is R2-2.
- **Requesters cannot choose an asset themselves** when making a request (a form question of type *asset* is planned with QR in R2-2); staff set it.
- **Existing organisations** must add the new permissions to their GA roles by hand (runbook).
- The import report lists at most 500 problem lines; fix those and check again.
- Not measured: the register at hundreds of thousands of assets (indexes are in place for type, place, person and warranty; the place filter expands the place tree in memory, fine for thousands of places).

## Verification of the functional behaviour
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16, Testcontainers) | **604 passed, 0 failed** (595 before; +9 `AssetTests`) |
| `tsc --noEmit`, `eslint`, `next build` | pass (one pre-existing `<img>` warning in the app icon) |
| Full Playwright suite (light + dark accessibility, responsive) | **185 passed, 0 failed** (176 before; +3 assets, +6 accessibility pages) |

## Next
R2-2: QR labels and reporting a problem from the asset, warranty reminders, failure count and the "replace vs repair" hint (AST-002/003/005).
