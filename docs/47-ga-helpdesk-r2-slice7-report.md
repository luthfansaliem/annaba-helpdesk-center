# 47 — GA Helpdesk, R2 slice 7: knowledge base

Status: **implemented and verified** (API and Playwright suites on the final code; see the table at the end). Seventh slice of release R2 (`40-ga-helpdesk-r2-plan.md`): **KB-001** (articles with text and images, categories, visibility, versions, review dates, helpful / not helpful), **KB-002** (suggestions while making a request, deflection count) and **KB-003** (policy and SOP documents on articles, articles linked to services).

## What was done
1. **Knowledge base** (*Requests → Knowledge base*, for everyone): search by any words of the title, summary or text, filter by category, open an article, say whether it helped, and — for articles linked to services — go straight to *Ask for …*.
2. **Writing** (`kb.write`, GA Agents and up): title, one-sentence summary, category, who can read it (everyone, or helpdesk staff only), a review date, the services it belongs to, and the text in simple Markdown with a **preview**. Files can be added once the draft exists — pictures (shown in the text with `![alt](file:…)`, the editor gives the code to copy) and documents such as policies and SOPs (listed under the article).
3. **Versions and publishing.** Every save that changes the text is a new, permanent version. Readers only ever see the **published** version, so a published article can be improved in a draft without readers seeing half-finished text. Publishers (`kb.publish`, GA Supervisor and Manager) publish the latest version, archive or restore an article, and manage categories. Writers can look at any earlier version.
4. **Before you ask (deflection).** On the request form, articles linked to that service appear first, then articles matching what the person types (after a short pause). Opening one shows it beside the form with *Yes, this solved it* — which sends no request and is counted — or *No, continue with my request*.
5. **How articles are used**, for writers: views, helpful / not helpful, *solved without a request*, and *review due* when the review date has passed (also a list filter).
6. **Safe text.** Markdown is turned into page elements by the web app, never into raw HTML: script, HTML tags and `javascript:` links stay plain text; links may only go to `https:`, `http:`, `mailto:` or the site itself; images only show the article's own files.

## Rules worth knowing
| Rule | Why |
|---|---|
| Reading needs only a session; staff-only articles need `kb.read`; drafts and archived articles are writers' only (others get `404`). | Self-service is for everyone without giving the User role new permissions. |
| Category, visibility, review date and services change at once; the **text** goes through versions and publishing. | What readers see is reviewed; housekeeping isn't. |
| A save that doesn't change the text keeps the version number. | Short, meaningful history. |
| Publishing bumps the article's version, so an editor open during publishing must reload. | Never overwrite text someone just published. |
| Files: same checks as ticket files (type list, 8 MB, virus scan), at most 20 per article, served with the sandboxing download headers; readers can open them only when they can read the article. | One file pipeline, no back door through files. |
| A suggestion's *this solved it* adds one to the article's count. | Deflection is the measure the PRD asks for (KB-002). |

## Tests added
**API — `KnowledgeTests` (3):** validation and permissions (requesters can't write, agents can't publish); a draft is invisible to readers; publishing shows it; an improvement stays a draft while readers keep the published text; a stale save is refused; earlier versions are readable by writers; a save without text change keeps the version and replaces the services; publishing again shows the new text; archived articles disappear for readers and are listed for writers; restore. Staff-only articles are not found, opened or suggested for requesters but are for agents; one helpful answer per person, changeable, counts only for writers; views; suggestions for a service and by typed words; *solved* counts and is refused for an article the person can't read. Categories only for publishers, duplicate names refused, an archived category can't be chosen; files on a draft are hidden from readers until published, then served with sandboxing headers; disallowed and infected files refused; only writers delete files; review-due and category filters.
**Playwright — `kb.spec.ts` (1) and `/kb` in the accessibility sweep (light and dark):** an admin writes an article with a preview (heading, bold, a `javascript:` link left as text, a safe link kept) and a linked service, saves the draft, publishes version 1; a requester searches, opens it, sees no *Edit*, answers *Yes*, sees *Ask for …*; on that service's request form the article is suggested, opened beside the form, and *Yes, this solved it* leaves the form without a request; the writer sees *1 / 0* helpful and the solved count; requesters can't write (`403`); the list and the article pass the accessibility check.
**Unit — `markdown.test.tsx` (3):** written for the renderer (structure, no markup or unsafe links, file images), but **not run here**: Vitest does not start on this machine (its `rolldown` native binding is missing from `node_modules`, a local install problem unrelated to this slice). The same safety points are covered by the Playwright test above.

## Decisions and known gaps
- **No public (signed-out) articles**: the PRD's "public" visibility needs a signed-out portal, which the product does not have; *everyone* means everyone signed in to the organisation.
- **Markdown, not a rich-text editor**, with a preview; tables are not supported.
- **No review reminders** yet (the list shows *review due*); **no comments on articles**; search is word matching, not ranked full-text search.
- **Existing organisations** must add the permissions to their GA roles by hand (runbook).

## Verification of the functional behaviour
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16, Testcontainers) | **630 passed, 0 failed** (627 before; +3 `KnowledgeTests`) |
| `tsc --noEmit`, `eslint`, `next build` | pass (one pre-existing `<img>` warning in the app icon) |
| `vitest` (web unit tests) | **not run**: Vitest fails to start on this machine (missing `rolldown` native binding) |
| Full Playwright suite (light + dark accessibility, responsive) | **209 passed, 0 failed** (206 before; +1 knowledge base, +2 accessibility pages) |

## Found and fixed while testing
- A help line ("0 chosen") with the standard negative top margin overlapped the last service in the editor's list and blocked clicks on it; it now sits above the list.

## Next
R2-8: cost and budget — cost lines rolled up per ticket, asset, site and vendor, budgets per cost centre, cost reports.
