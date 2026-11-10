# 37 — GA Helpdesk, R1 slice 14: backups that include the files, restore drill, files check

Status: **implemented and verified** (a real restore drill, API and Playwright suites on the final code; see the table at the end). Part of release R1 (`23-ga-helpdesk-prd.md`, §14 pilot checklist): "restore drill of the files volume together with a database dump".

## The gap
Until now the nightly backup saved only the database. Attachments live on a separate volume, so a restored database showed files whose bytes were gone (downloads answered 404) and nobody could tell how many or which tickets. Slice 13 also noted that stored files with no database row (left by a crash) were never reclaimed.

## What was done
1. **Backups carry the files.** The compose `backup` service mounts the files volume read-only and, right after each `pg_dump`, writes `annaba_digital-<stamp>.files.tgz` with the same stamp (temporary name first, renamed only on success; pruned with the dumps). `deploy/pull-backups.sh` copies both, checks the archive with `gzip -t`, and prunes both.
2. **A restore drill you can run anywhere with docker:** `deploy/restore-drill.sh <dump> [files.tgz]` restores into a throw-away PostgreSQL container, unpacks the archive into a temporary folder and checks that **every live attachment row has its bytes**. It prints the latest migration and row counts, names the missing files, cleans up after itself, and exits 0 / 1 / 2.
3. **A files check in the product.** *Administration → Data Retention → Stored files* (permission `retention.manage`) compares this organisation's live attachment rows with the volume: how many files are recorded, which are **missing** (with their tickets), and which stored bytes are **unreferenced**. *Remove unreferenced files* deletes only those, audited and behind a fresh password check.
4. The storage port gained `ListAsync(prefix)` (local-disk adapter lists one organisation's folder; only keys of the exact shape).

## Rules of the check
| Rule | Why |
|---|---|
| Missing = a live row (not soft-deleted) whose bytes are absent. The first 50 are listed with file name and ticket link; the count is exact. | After a restore you need to know what to chase, not just that something is wrong. |
| Unreferenced = bytes under this organisation's folder with no live row **and last changed more than an hour ago**. | An upload stores its bytes just before its row is saved; a file in flight must never be deleted. Also covers rows soft-deleted whose bytes survived a crash (the gap noted in slice 13). |
| Another organisation's folder is never listed or touched. Only keys of the form `{32 hex}/{32 hex}` count (temporary or foreign files on the volume are ignored). | Tenant isolation, and no surprises from stray files. |
| Cleaning re-scans at the moment it runs, tolerates a file the volume refuses (counted in `failed`), and is audited with counts only (`files.orphans.clean`). | Same safety posture as the retention run. |
| The dump is taken **first**, the archive right after. | Every row in the dump then has its bytes in the archive; the only drift is files removed or uploaded in the minutes between, which the check classifies correctly (unreferenced, or missing for rows that no longer matter). |

## Verification of the drill (real, on this machine)
- A dump of the e2e database (5 organisations, 15 users, 14 tickets, last migration `DataRetention`) with its files archive → **DRILL PASSED** (3 attachments checked, 0 missing, 33 files in the archive).
- The same dump with an archive lacking one live file → **DRILL FAILED**, naming the file, exit 1.
- Dump only → passed with an explicit "files were NOT checked" warning. A text file renamed `.dump` → refused (exit 2). No throw-away container is left behind in any case.
- The first run of the script **failed for two real reasons and the drill reported them**: Windows paths for `docker cp`, and a wrong column name for the migration history (`migration_id`). Both fixed.
- The compose backup step (`tar czf` then rename, inside `postgres:16-alpine`, against a real folder) produced a valid archive.

## Tests added
**API — `FileCheckTests` (4):** a consistent volume reports nothing and only retention managers may look; bytes gone from the volume are listed with file name and ticket and downloads answer 404; unreferenced bytes (old stray, soft-deleted row's bytes) are counted with their size and removed while a recent file, another organisation's file and live files stay, with an audit row; cleaning needs a fresh password check. **Playwright:** the retention spec also runs *Check stored files* (read-only, because the shared e2e volume may hold other tests' leftovers) with an accessibility pass.

## Decisions and known gaps
- **Not verified on Coolify.** The backup service now mounts `annaba-digital-files`; after the next deploy confirm that a `.files.tgz` appears next to the dump (volume names carry the project prefix). A scheduled off-site pull and a full recovery of the VPS were not rehearsed.
- **The archive is a plain tar of the volume**, not incremental: it grows with the files volume (8 MB per file, 20 per ticket). For large volumes an object store with its own versioning replaces it (the storage port is the seam).
- **The check scans one organisation at a time** and holds that organisation's file list in memory (about 100 bytes per file): fine for hundreds of thousands of files, not designed for tens of millions.
- **Missing files cannot be repaired from the product**: the check tells you which, the real fix is the matching archive. A ticket whose file is gone simply shows it as unavailable.
- Platform-wide orphans of organisations that no longer exist are not covered (an organisation delete is not a feature yet).

## Verification of the functional behaviour
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16, Testcontainers) | **589 passed, 0 failed** (585 before; +4 `FileCheckTests`) |
| `tsc --noEmit`, `eslint`, `next build` | pass (one pre-existing `<img>` warning in the app icon) |
| Full Playwright suite | **167 passed, 0 failed** (the retention spec also runs the file check) |

## Next
Remaining pilot-checklist items: in-app help for the three personas and per-tenant e-mail wording (NTF-002); or release R2 (work orders, assets, preventive maintenance).
