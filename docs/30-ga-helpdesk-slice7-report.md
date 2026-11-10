# 30 — GA Helpdesk, R1 slice 7: attachments and the file question

Status: **implemented and verified** (API against PostgreSQL, browser flows and accessibility in Chromium). Part of release R1 (see `23-ga-helpdesk-prd.md`). Requirement covered: **TKT-005** (attachments behind a storage port, type and size allow-list, scan hook), plus a `file` question type for service forms (CAT-003 extension).

## What was built
| Area | Where |
|---|---|
| `FileRules`: the pure rule for what is accepted (extension allow-list **and** matching content, size, safe names) | `api/src/AnnabaDigital.Domain/FileRules.cs` |
| `file` question type (answer = list of upload ids, `max` 1–10, default 5) | `Domain/Forms/FormEngine.cs` |
| `Attachment` entity and `attachments` table (migration `Attachments`) | `Domain/Entities/Tickets.cs`, `AnnabaDigitalDbContext.cs`, `Persistence/Migrations` |
| Ports `IFileStorage`, `IAttachmentScanner`; adapters `LocalFileStorage` (disk, atomic write) and `NoopAttachmentScanner` | `Application/Abstractions/Abstractions.cs`, `Infrastructure/Storage.cs`, `DependencyInjection.cs` |
| `AttachmentService`: store, list, open, delete, claim, purge | `Application/Helpdesk/AttachmentService.cs` |
| `TicketService`: attach / open / remove with ticket visibility, timeline entries, claim at creation | `Application/Helpdesk/TicketService.cs`, `ServiceFormService.cs` |
| Routes (see `08-api-spec.md` §12.5) | `Api/Endpoints/TicketEndpoints.cs` |
| Orphan purge, run by the existing daily worker | `Infrastructure/EmailInfrastructure.cs` (`TicketAutoCloseWorker`) |
| Starter forms: an optional "Photo of the problem" (max 3) on air conditioning, electrical, plumbing and furniture | `CatalogTemplates.cs` |
| UI: upload control for the `file` question (uploads as soon as a file is chosen, shows names and sizes, remove), **Attachments** panel on the ticket, builder support ("Most files per request") | `src/components/forms/dynamic-form.tsx`, `src/app/(app)/tickets/[id]/page.tsx`, `src/app/(app)/setup/catalog/[id]/page.tsx`, `src/lib/files.ts` |

## How a file is handled
1. **Accepted types** (allow-list, not a block-list): `.jpg .jpeg .png .gif .webp` (images), `.pdf`, `.docx .xlsx .pptx`, `.doc .xls .ppt`, `.txt .csv`. Anything else — including `.svg`, `.html`, macro documents (`.docm`), archives and programs — is refused whatever it contains.
2. **The content must match the extension.** The first 4 KB are checked against the format's signature (PNG, JPEG, GIF, WebP, `%PDF`, ZIP container for OOXML, OLE for legacy Office; text files must contain no NUL or control characters). A program renamed `photo.png` is refused.
3. **Size**: 1 byte to 8 MB (inclusive); an oversized file is refused after reading at most 8 MB + 1 byte. The request itself is capped at 10 MB. At most **20 files per ticket** and **30 unclaimed uploads per person**.
4. **The name is never trusted**: folders and unsafe characters are stripped, the length is capped at 150 and the extension kept. The stored key is generated (`{tenant}/{id}`, hex only); the storage adapter refuses any other shape, so a name can never walk the disk.
5. **Scan hook**: every file passes `IAttachmentScanner` before a byte is stored. The default adapter accepts everything — **production must register a real scanner** (ClamAV or a cloud service). A rejection is audited (`attachment.rejected`) and answered `422`.
6. **Fingerprint and audit**: SHA-256 is stored and written to the audit event (`attachment.add`, with name and size); removals are audited too (`attachment.delete`). The ticket timeline shows "Attached x (size)." / "Removed x.".
7. **Serving**: always through the API after a visibility check, never from a static path. The content type is ours (from the allow-list), `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; sandbox`, and only **images are shown inline**; everything else downloads. The file name travels as RFC 5987 `filename*`.
8. **Removal** deletes the bytes and keeps the row (soft delete). The uploader or staff holding `tickets.update` may remove; the requester cannot remove staff's file. Finished tickets (closed, cancelled, rejected) take no new files and lose none (`409`).
9. **Form uploads**: a file chosen on a request form is uploaded at once (`POST /api/uploads`, no ticket yet). Creating the ticket **claims** the ids inside its own transaction — only the uploader's own, unclaimed, not-deleted files qualify; a file can be claimed once. Unclaimed uploads older than 24 h are deleted (bytes and rows) by the daily worker.
10. **Tenants**: the row is tenant-filtered like everything else, and a ticket from another organisation answers `404`.

## Verification
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16, Testcontainers) | **544 passed, 0 failed** |
| 56 pure tests (`FileRulesTests`, FormEngine, templates): every allowed type, a program renamed to each type, empty/oversized boundary, name sanitising, the file question's parse and definition rules | pass |
| 8 integration tests (`AttachmentTests`): attach + list + timeline + audit with fingerprint; download headers; refused (.exe, .svg, disguised program, empty, > 8 MB, scanner marker, > request limit); 20-file cap and room after removal; finished ticket `409`; stranger / other team / anonymous / other tenant; uploader vs staff removal; bytes really deleted; form uploads claimed once and only by their owner; purge after a day while claimed files stay; pending-upload cap | pass |
| `tsc --noEmit`, `eslint`, `next build` | pass (one existing `<img>` warning) |
| Playwright `attachments.spec.ts` (photo question end to end incl. refused files and the two-file limit; staff add a PDF; served headers; removal with confirmation; a stranger gets `404`; builder limit validation; axe on the request form and the ticket) | **3 passed** |
| Full Playwright suite (one shared database) incl. axe light + dark, responsive | **147 passed, 0 failed** |

## Decisions and known gaps
- **Storage** is a local folder (`Storage:Path`, default `data/files`). In production point it at a **persistent volume** (or implement `IFileStorage` for S3/Azure Blob — the port is three methods). Back it up together with the database.
- **No real antivirus yet**: the hook is in place and tested, the default scanner is a no-op. Treat it as a deployment task for the staging runbook.
- **Not built (TKT-005 wish list)**: signed, expiring download URLs (downloads are authenticated by session instead), EXIF/GPS stripping, image thumbnails, per-tenant storage quotas, and attaching to individual comments.
- The whole file is buffered in memory while it is checked (≤ 8 MB) so that the signature, hash and scan see the same bytes; streaming to disk with a rolling hash is the upgrade if the limit is ever raised.
- Next.js' middleware proxy buffers request bodies up to its own limit (10 MB by default); the 8 MB file cap plus multipart overhead stays below it. Raise both together if the cap is raised.
- Removed files cannot be recovered; a retention/legal-hold policy for ticket data is part of the later retention work.

## Next
The R1 hardening pass (onboarding wizard, GA role seeds review, helpdesk staging runbook including the scanner and storage volume, end-to-end demo data), before release R2 (work orders, assets, preventive maintenance).
