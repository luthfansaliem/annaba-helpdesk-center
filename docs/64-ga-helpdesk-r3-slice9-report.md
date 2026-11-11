# 64 — GA Helpdesk R3 slice 9: directory (SSO groups, SCIM, HR sync)

Status: **implemented, verification below**. Ninth slice of R3 (`55-ga-helpdesk-r3-plan.md`). It covers INT-003 (directory groups) and the sync part of ADM-004.

## What was done
1. **Groups that give roles** (*Administration → Directory*, `directory.manage`).
   - A mapping says "people in directory group X get role Y". The group is either a value of the SSO token's groups claim or the name of a group the identity provider pushes over SCIM. Names are compared without regard to case.
   - Roles that appear in any mapping are **managed by the directory**: given when the person is in a mapped group and taken away when they leave it. Roles given by hand that no mapping uses are never touched.
   - **Super Admin can never be mapped.** You may only map a role you could grant yourself, the same rule as assigning roles.
   - Adding or removing a mapping needs a recent re-authentication. Both are audited and raise a privilege-change security event.
2. **SSO groups at sign-in.**
   - The OIDC client reads the groups claim (`Sso__GroupsClaim`, default `groups`; a string or a list).
   - At each SSO sign-in the person's managed roles follow their groups. A token *without* the claim leaves roles as they are, so a provider that stops sending it does not strip everyone's access.
   - Changes are audited (`directory.roles_synced`, with what was added and removed) and raise a security event.
3. **SCIM 2.0** (RFC 7643/7644) at `/api/v1/scim/v2`.
   - **Access:** an API key with the new `directory.write` scope, made under *Integrations* by someone holding `directory.manage`.
   - **What it supports:**
     - Users and Groups: list with `filter=attr eq "value"` and paging, get, create, PUT and PATCH (add, replace and remove, with or without a path, including `members[value eq "…"]`), delete.
     - `ServiceProviderConfig`.
     - The enterprise extension: department, employee number, manager.
     - Errors in the SCIM error format (`uniqueness`, `invalidFilter`, `mutability`, …).
   - **New people** are active at once with the base User role and sign in with SSO. They get no password and no invitation; the provider vouches for the e-mail.
   - **Turning people off:** `active: false` suspends and DELETE deactivates. Both end the person's sessions; DELETE also takes them out of every SCIM group.
   - **What SCIM refuses:**
     - suspending or deactivating a Super Admin, or the key's owner;
     - changing an existing person's e-mail (`userName`), which would let a provider move an account to another mailbox.
   - **Groups:** membership changes re-sync the members' managed roles. Renaming a group re-syncs everyone in it.
4. **HR sync (CSV)**, on the page and on the public API (`POST /api/v1/directory/sync`, scope `directory.write`) for a nightly job.
   - **Columns:** e-mail, employee ID, department, job title, manager e-mail, site code, status. An empty cell leaves the value as it is.
   - **Preview first, then apply.** Each row is validated as a whole (unknown manager, own manager, unknown site, too long) and only clean rows are applied.
   - **Suspending:** people marked *inactive* are suspended and their sessions end. With *Suspend people who are not in the file*, active people missing from it are suspended too, except Super Admins, the person running the sync and vendor accounts.
   - **Nobody is created here;** unknown e-mails are reported. People arrive by invitation or SCIM.
   - Applying on the page needs a recent re-authentication; the API key route has no session, so it is exempt.
5. **Employee data on the profile:** employee ID, home site and the identity provider's id, shown on the person's page.
6. **Audit:**
   - `directory.mapping_add`, `directory.mapping_remove`, `directory.roles_synced`;
   - `directory.scim`, for every SCIM change;
   - `directory.sync`;
   - `user.create` (mode scim), `user.suspend` and `user.deactivate` (source scim or directory sync).

## Decisions
- **One mapping table for SSO and SCIM.** A group is a name either way. Use one source per organisation: if both are used, the most recent SSO sign-in and the most recent SCIM change each set the managed roles from their own view.
- **Managed roles only.** Mapping a role hands that role to the directory completely: assigning it by hand is undone at the next sync. Roles that no mapping uses are untouched.
- **SCIM over the existing API keys.** One credential system with scopes, rate limits, revocation and "stops when the owner leaves", instead of a second token store. The base URL and key go into the identity provider as a bearer token.
- **No people created by CSV.** HR files are often partial or stale. The sync only updates and suspends, and creation stays with invitations (doc 18) or SCIM.

## Tests added
- **API, `DirectoryTests` (3, isolated organisations):**
  - **SSO groups:**
    - Mappings need `directory.manage`; Super Admin is refused, and a User Admin cannot map the GA Agent role (they don't hold its permissions). A duplicate mapping is refused regardless of case.
    - Signing in with `ga-agents` gives GA Agent, a token without the claim leaves it, and signing in without the group takes it away, keeping the hand-given User role.
    - The change is audited, and after the mapping is removed the group no longer gives the role.
  - **HR CSV:**
    - The preview counts 2 updated, 1 suspended, 1 not in the app and 3 not applied (Super Admin, own manager, duplicate row), and writes nothing.
    - Applying first asks for re-authentication. Then it writes the employee ID, home site, department and manager, suspends the leaver and leaves the Super Admin active.
    - *Suspend missing* suspends the person who was left out, but neither the sync's runner nor the Super Admin.
    - A file without an e-mail column is refused.
  - **SCIM:**
    - Access: no key gives 401, a key without the scope 403, and `ServiceProviderConfig` answers.
    - Creating a person with name, title, department and employee number makes them active with the User role. A duplicate gets 409 `uniqueness` in the SCIM error format.
    - Filters on userName and externalId work, and an unsupported filter gets `invalidFilter`.
    - PATCH of the title works; changing the e-mail gets `mutability`.
    - A group gives GA Agent; removing the member takes it away and adding them back restores it.
    - `active: false` suspends and `true` reactivates.
    - DELETE deactivates and drops the managed role; deleting a Super Admin gets 409; an unknown id gets 404. Changes are audited.
- **Playwright, `directory.spec.ts`:**
  - The admin sees the SCIM base URL, maps a group to GA Agent and removes it again.
  - They preview an HR file (1 updated, 1 not in the app) and apply it.
  - The page passes the accessibility check before and after the preview.

## Known gaps
- **No scheduled pull** of a file from SFTP or a URL. A nightly job posts the CSV to the public API instead.
- **SCIM bulk, sorting and ETags** are not supported (as advertised in `ServiceProviderConfig`). Filters are `eq` only, which is what Entra ID and Okta send.
- **The 120 requests a minute per key** also applies to SCIM. A first full push of a large directory is throttled and the provider retries.

## Verification
| Check | Result |
|---|---|
| `tsc --noEmit`, `eslint` | pass |
| `next build` | pass |
| `dotnet test api/AnnabaDigital.sln` | **668 passed, 0 failed** |
| Full Playwright suite | **234 passed, 0 failed** (with the R3-6 inventory fix) |
