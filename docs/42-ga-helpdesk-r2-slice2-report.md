# 42 — GA Helpdesk, R2 slice 2: QR labels, reporting from an asset, warranty reminders

Status: **implemented and verified** (API and Playwright suites on the final code; see the table at the end). Second slice of release R2 (`40-ga-helpdesk-r2-plan.md`): **AST-002** (QR labels, scan to open the asset and report an issue), **AST-005** (warranty reminders) and the rest of **AST-003** (failure frequency and the "replace vs repair" hint).

## What was done
1. **Label sheets.** *Assets → Labels* (by type and/or place) or *Print label* on one asset: a printable grid of labels, each with a QR code, the tag, the name, the type and the place, and "Scan to report a problem". Printing hides the app around the sheet (three labels per row on paper). At most 500 per sheet. The QR is drawn in the browser (the `qrcode` library already used for two-step verification).
2. **The scan page** (`/a/<tag>`, what each QR encodes). Anyone signed in to the organisation sees the asset's name, tag, type, place and status, and **Report a problem with this**; people who may read the register also get *Open in the asset register*. A retired asset says it is out of service and offers no report. Someone not signed in is asked to sign in first and then lands back on the asset.
3. **Reporting from an asset.** *Report a problem* (on the scan page or the asset page) opens the service catalog with a banner "This request is about AST-000123 · Copier" (with a way out: "It is not about this asset"); the chosen service's form keeps it, **starts at the asset's place**, and the request is saved linked to the asset. Requesters can only link an asset this way (they never see the register).
4. **"Fails often."** The asset page counts the requests about it in the last 12 months (all of them, as a number only) and, from 3 on, says so; if the warranty has also ended it suggests replacing rather than repairing. The register has a *Fails often* filter (also in the export).
5. **Warranty reminders.** Once a day-ish (with the hourly ticket worker), people whose roles hold `assets.manage` are told when a warranty **ends within 30 days** and when it **has ended** — once per warranty date; changing the date starts over. More than 5 at once become one summary linking to the register filtered on warranties. A warranty that ended more than 30 days before anyone could be told (old assets imported in bulk) is recorded silently instead of flooding the bell. New notification kind `asset.warranty` (group *Assets*), in the app and by e-mail by default, switchable per person.

## Rules worth knowing
| Rule | Why |
|---|---|
| The scan lookup returns only name, tag, type, place and status, to anyone signed in to the **same** organisation; another organisation's tag is "not found". | The label is on the thing itself; the register's details (cost, person, history) stay behind `assets.read`. |
| A request can name an asset only if it is in the register and not retired (`422 assetId`). | Nothing is reported against scrapped equipment. |
| Reminders are claimed with one conditional update per asset, so two instances never both send. | The same rule as every worker in the product. |
| The "fails often" number counts every request about the asset, but only as a number. The list of requests still follows queue visibility. | A useful signal without leaking other teams' tickets. |
| Labels encode the installation's own address (`<origin>/a/<tag>`). | Labels printed on staging point to staging: print the real ones from production. |

## Tests added
**API — `AssetScanTests` (5):** the card by tag (any case) and by id for a requester (no register access) and for staff, unknown tag, signed-out, the register itself still forbidden; reporting a problem links the asset; a retired asset is shown as retired and cannot be reported on; **another organisation** gets "not found" for the card by tag or id, the asset and its labels; the "fails often" count over 12 months (an older request excluded), the hint and its stronger form after the warranty ended, the filter; label sheets by chosen ids (tag order, bad ids ignored) or by type, forbidden without the register; warranty reminders (an isolated database): ending soon and just ended are announced, a far one and one ended long ago are not, by e-mail too, once per date, a new date starts over, and six at once become one summary.
**Playwright — `asset-scan.spec.ts` (2) and the labels page in the accessibility sweep (light and dark):** a label is printed from the asset with a real QR image; a requester opens the scan page (no register link), reports a problem, the form keeps the asset and starts at its place, and the request shows the asset; the asset lists it with "1 in the last 12 months"; an unknown tag says so and a retired asset offers no report.

## Decisions and known gaps
- **The camera is the phone's**: the app does not include a scanner; any QR camera opens the link. A scanner inside the app (for an offline PWA) is for later.
- **Barcode (1-D) labels** are not offered; QR only. Label size is fixed (three per row on A4/Letter); no label-printer templates yet.
- **No reminders for contracts** (vendor contracts arrive with R2-4) and no reminder to the asset's holder, only to asset managers.
- **The replace-vs-repair hint uses counts, not cost**: repair costs come with work orders (R2-3) and will make it sharper.
- The reminder runs with the hourly ticket worker; it is cheap when nothing is due, but its first run after an import of thousands of assets with near warranty dates sends one summary per manager, by design.

## Verification of the functional behaviour
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16, Testcontainers) | **609 passed, 0 failed** (604 before; +5 `AssetScanTests`) |
| `tsc --noEmit`, `eslint`, `next build` | pass (one pre-existing `<img>` warning in the app icon) |
| Full Playwright suite (light + dark accessibility, responsive) | **189 passed, 0 failed** (185 before; +2 asset scan, +2 accessibility pages) |

## Next
R2-3: work orders and checklists — child work orders on a ticket with their own assignee, checklist (required items block completion), labour time and parts/cost lines, photo evidence (TKT-006/007/014).
