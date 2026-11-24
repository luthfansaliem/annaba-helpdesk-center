# 71 — Spike H00: can this application be entered from Annaba Workspace as it is?

Date: 2026-11-24. Branch `spike/h00-core-contract`, from `main` at `15efc13`. **A trial, not a release: not to be merged or deployed.** It answers the question behind decision CD-2 of the platform audit (`annaba-core-admin`, `docs/roadmap/CODEBASE_DECISION.md` §5): continue this codebase and fit it to the platform (H1), or build a new application from the template and port the domain (H2).

## 1. Answer

**H1 is feasible, and the hard part is smaller than expected.** A member arrives from Workspace with a launch code and uses the ordinary application, in their own Workspace only, after 173 lines of new application code and one schema change. Nothing behind the session had to change: the same tenant filter, the same permissions, the same endpoints.

The reason is one design fact of this codebase: **tenancy is decided in one place.** Every one of the 96 tenant-scoped entity classes is filtered and stamped by the database context from `ITenantContext`, which is set once per request by the session handler. So "a Workspace is a tenant" and "a member is a user of that tenant" is a mapping at the door, not a rewrite of 449 routes.

What H1 still costs is real and is listed in §4. The recommendation is to continue with H1.

## 2. What was built

| Piece | File | Lines |
|---|---|---|
| The seam: entitlement → tenant; launch code → user of that tenant → ordinary session | `api/src/AnnabaDigital.Application/Platform/WorkspaceBridge.cs` | 104 |
| The launch exchange with Core (service key; a timeout or 5xx is never a sign-in) | `api/src/AnnabaDigital.Infrastructure/Integrations/CoreExchange.cs` | 28 |
| `POST /platform/v1/entitlements` (Core's HMAC signature, five minutes of tolerance) and `POST /api/launch` | `api/src/AnnabaDigital.Api/Endpoints/WorkspaceBridgeEndpoints.cs` | 41 |
| Schema: `tenants.workspace_key`, `tenants.entitlement_revision`, `users.account_ref`; **`users.email` unique per tenant instead of in the whole database** | migration `SpikeWorkspaceBridge` | — |
| The journey as a test, with a stand-in for Core | `api/tests/AnnabaDigital.Tests/SpikeWorkspaceBridgeTests.cs` | 89 |

Mapping used: Workspace key → tenant (created once, with the same system roles and starter set-up a tenant made by hand gets); `account.ref` → user (never the e-mail address); the role granted in Workspace → one system role (`app_admin` → Super Admin of that tenant, `operator` → GA Agent, anything else → the plain employee role), set again at every launch.

## 3. What the test shows

`A_member_enters_from_Workspace_and_works_in_their_own_Workspace_only`, passing:

- Entitlement: refused with a wrong signature or an old timestamp; applied once; an older revision arriving late does not lower it. A launch before any entitlement is refused (`not_provisioned`).
- Launch: refused without the state the code was issued for, for another application's code, and on a second use.
- Behind the session: `/api/auth/me` names the person and address Core gave; the administrator creates and publishes a service, an employee raises a request on it, the requester and the administrator can open the ticket, an agent outside the owning team cannot (the application's own rule, untouched), an employee cannot list users.
- **One person in two Workspaces**: the same account and the same e-mail address become two users in two tenants, and neither Workspace can open the other's ticket.
- A later launch with another role and another address in Workspace: the same user, with the new role and address.

## 4. What it measured about the cost of H1

| Fact | Number | Meaning |
|---|---|---|
| Tenant-scoped entity classes, all filtered in one place | 96 | The reason the seam is small |
| `users.email` unique in the whole database | 1 index | **The one structural conflict.** A person belongs to several Workspaces. Changed here to "unique per tenant" |
| Places that look a user up by e-mail outside a tenant | 14 of 56 `IgnoreQueryFilters()` uses, in 33 files | Each must be read: sign-in, invitation, SSO, SCIM, e-mail intake, approval by e-mail link. Sign-in, invitation and SSO disappear under Workspace; e-mail intake and approval links must find the tenant another way (the mailbox, the link's own token) |
| Routes that exist only for local identity | 102 of 449 (auth 7, MFA 6, SSO 2, account 7, users 21, roles 10, tenants 6, access requests 8, access reviews 7, account closures 7, directory 19, set-up 2) | To be switched off or replaced by Workspace under the platform. Their screens too |
| Properties that point at a person | 64 | They keep working: a member is a real `users` row |
| Test classes that sign in with a password | 62 of 76 | The suite stays valid as long as local sign-in exists in the Testing environment; under the platform it needs a "launch as" helper instead |
| Full API suite on this branch, with e-mail unique per tenant | **672 of 672 passed** (670 before, plus the journey test and nothing lost) | Making an address unique per tenant broke nothing the suite checks. On the first run one test failed, as it should: the inventory of anonymous routes, which did not yet list the two new ones |

## 5. What the spike did not do (the rest of H1)

1. **Revocation on the next request.** A session made by a launch lives until it expires. The contract wants the access check asked per request and per job (with a few seconds of cache) and the session dropped when the membership or grant revision changes. The session validator is one place (`SessionService.ValidateAsync`), so it is a contained change, but it is not done.
2. **People who have never opened the application.** An approver, a manager, or someone a request is raised "on behalf of" must exist as a user before their first launch. That needs Core's member lookup (R02, built on a Core branch) and a just-in-time user for a chosen member.
3. **One cookie.** The session cookie is one name for the whole host, so one browser holds one Workspace at a time; opening a second Workspace replaces the first. Asset uses a cookie per Workspace. Acceptable for a first release only if stated; otherwise the cookie name has to carry the Workspace.
4. **The web side.** No `/launch` route in the Next.js app (with the state cookie, as in Asset), and the sign-in, invitation, MFA, user and role screens are still there.
5. **Plans, features, quotas, usage.** Nothing is enforced or reported: no agent-seat meter, no feature gates, no restriction (read-only), no downgrade preflight, no purge or closure answer.
6. **Roles.** Three roles are mapped by a hard-coded switch. The real list (supervisor, manager, receptionist, technician, per department) has to be published to Core's catalogue as role templates, and scoped roles (site, team) have no counterpart in a Workspace grant yet.
7. **The asset, maintenance and inventory modules** are untouched; their fate is decision CD-3.
8. **Existing data.** The demo tenants and their local accounts have no Workspace. Linking an existing tenant to a Workspace is a migration with its own rules.
9. **Security review of the seam.** Written in a day; the signature check, the slug derived from a Workspace key, and the role reset at launch need a second reader.

## 6. Estimate for H1 after this spike

Reasoned, not measured, in working days for one engineer who knows both codebases: revocation and access check 2; member lookup and just-in-time users 2; web launch route and removal of local identity screens 3; e-mail lookups outside a tenant 2; entitlement, restriction, preflight, purge and closure 3; plans, seat meter and feature gates 3; role templates and scope 2; test harness ("launch as") and regression 3; browser journey against a real Core 2. **About 22 days**, before the product work of the PRD (departments, incident type) and before CD-3.

H2 (a new application, porting the domain) would have to carry 449 routes, 47 migrations of schema and 567 test methods across a different identity model. This spike gives no number for it; it removes the main reason to choose it.

## 7. How to run it

```bash
npm run api:test -- --filter "FullyQualifiedName~SpikeWorkspaceBridgeTests"
```

Configuration (all four needed, otherwise both routes answer "not configured"): `Core__BaseUrl`, `Core__ServiceKey` (`helpdesk:<secret>`), `Core__AdapterSecret` (≥ 32 characters), `Core__AppKey` (default `helpdesk`).
