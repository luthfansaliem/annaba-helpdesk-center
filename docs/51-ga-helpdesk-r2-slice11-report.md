# 51 — GA Helpdesk, R2 slice 11: vendor portal

Status: **implemented and verified** (API and Playwright suites on the final code; see the table at the end). Last slice of release R2 (`40-ga-helpdesk-r2-plan.md`): vendor sign-ins limited to their own work orders (PRD open question **Q6**, the vendor part of VND).

## What was done
1. **A Vendor role** (seeded for every organisation — existing ones get it at the next start, as missing system roles are added): only `workorders.read` and `workorders.complete`.
2. **Portal people on the vendor's page** (*Vendor portal* panel, `vendors.manage`): invite the vendor's technician as a user with the Vendor role (*Users → Invite*), then give them access by e-mail on the vendor's page; remove access there too. Only people whose rights are nothing more than the Vendor role can be linked (staff can't be turned into vendor accounts by accident), one vendor per person.
3. **What a vendor's person sees**: after signing in they land on *My work orders* — the work orders given to their company, and nothing else in the menu but their notifications. They open a work order, start it, put it on hold, tick the checklist, add before/after photos, log time and complete it with a note — exactly the work-order rules of R2-3. They do not take the work order over: the organisation's coordinator stays its assignee. They never see costs, and they can't cancel.
4. **What they can't reach**: everything else. A server-side allow-list lets a vendor's person call only sign-in and profile endpoints, notifications and work orders; any other endpoint — requests, tickets, the people directory, places, the catalog, the knowledge base, reports, settings — answers `403`, whatever a role might grant. The work-order list and page show the ticket's number and subject as context, without a link into the ticket.

## Rules worth knowing
| Rule | Why |
|---|---|
| A vendor's person's scope is **their vendor's work orders**, decided by the link — not by tickets or teams. | Vendors work across the organisation's sites but must not see its requests. |
| The allow-list is enforced on the server for every request (the link is cached 30 seconds). | A new endpoint is closed to vendors by default. |
| Linking refuses anyone with more than the Vendor role's rights. | Keeps vendor and staff accounts apart. |
| "Mine" in the work-order list means the company's work for a vendor's person. | They are never the assignee. |
| Removing access ends the work-order view at once; the rest stays closed for up to 30 seconds after. | The cache can only make access narrower, never wider. |

## Tests added
**API — `VendorPortalTests` (1):** linking refuses an unknown e-mail (invite first), a staff member, and a person without the Vendor role; only vendor managers link; one vendor per person; the vendor page lists portal people only to managers; *me* names the vendor. Of three work orders (the vendor's, another vendor's, in-house) the vendor's person lists only theirs and gets `404` for the others; they start it (the agent stays the assignee), tick the checklist, log time, complete it; adding costs and cancelling are refused. Locations, the catalog, the people directory, the knowledge base, tickets, costs, maintenance and vendors all answer `403`, as does making a request; notifications work; an ordinary person is unaffected; after access is removed their work-order list is empty. `VendorTests` and `WorkOrderTests` still pass.
**Playwright — `vendor-portal.spec.ts` (1):** an admin invites a person with the Vendor role, gives them access on the vendor's page; the person signs in and lands on their work orders, sees only *My work orders* in the menu (no *New request*), sees the vendor's work order but not the in-house one, with no link into the ticket; the page passes the accessibility check; they start the work order; a call to the places list answers `403`.

## Decisions and known gaps
- **Two steps to onboard** (invite with the Vendor role, then link on the vendor's page); a one-step "invite to this vendor" is a later convenience.
- **Vendors don't see or upload invoices or quotes** in the portal (costs stay internal; vendor invoices are recorded by staff as cost lines).
- **No vendor-side notifications by e-mail for new work** yet: they see new work in the portal (in-app notifications reach them only when they are the assignee).
- **The ticket's subject** is shown to the vendor as context; requesters should not put personal data there (most request forms already ask for details separately).

## Verification of the functional behaviour
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16, Testcontainers) | **639 passed, 0 failed** (638 before; +1 `VendorPortalTests`) |
| `tsc --noEmit`, `eslint`, `next build` | pass (one pre-existing `<img>` warning in the app icon) |
| Full Playwright suite (light + dark accessibility, responsive) | **223 passed, 0 failed** (222 before; +1 vendor portal) |

## Found and fixed while testing
- The work-order list opens on *Mine* (assigned to me); a vendor's person is never the assignee, so their list was empty. *Mine* now means their company's work for them, and the list hides the Mine/Everything switch and the ticket link for them.
- `TenantTests` lists the system roles a new organisation gets; the Vendor role is new, so it was added to the expected list.
- In the full Playwright run the vendor-portal test hit the per-address limit on accepting invitations (10 per 15 minutes; every test comes from one address). Like `setup.spec.ts`, it now uses its own client address; it also gets 90 seconds (an e-mail, an acceptance and a second sign-in).
- Under the full suite's load the webhook test waited too briefly; its waits now allow 30 seconds. One full API run also showed a single intermittent failure in an older test (`ApprovalAndNotificationTests`, replies and notes), which did not repeat in the next full run; it waits on e-mail delivery and is sensitive to load. Noted, not changed.

## Release R2
All eleven slices are in (reports 41–51). R2's exit criteria in the PRD also name a **penetration test** with High/Critical findings closed and **load-test budgets** met; neither has been run — they need an external tester and a production-sized environment, and are the next step before calling R2 released.
