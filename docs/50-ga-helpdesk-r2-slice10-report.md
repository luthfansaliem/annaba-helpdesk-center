# 50 — GA Helpdesk, R2 slice 10: API keys and webhooks

Status: **implemented and verified** (API and Playwright suites on the final code; see the table at the end). Tenth slice of release R2 (`40-ga-helpdesk-r2-plan.md`): **INT-001** (public REST API with per-organisation keys, scopes, rate limits, idempotency keys on create) and **INT-002** (HMAC-signed, retried, replay-protected webhooks for ticket events).

## What was done
1. **API keys** (*Administration → Integrations*, `integrations.manage`, a critical permission — in the seeded roles only Super Admin): a name, scopes (`tickets.read`, `tickets.write`, `catalog.read`) and an optional expiry. Making one needs a recent password re-entry; the key (`adk_…`) is **shown once** and only its SHA-256 is stored. The list shows the first characters, the owner, scopes, last use and expiry; a key can be **revoked** at once.
2. **A key acts as the person who made it, within its scopes.** It can never do more than that person may (visibility, permissions), and stops working when that person is suspended or leaves. It is accepted **only on the public API** (`/api/v1`): anywhere else it is not a session; and the public API takes only keys, never a browser session.
3. **The public API** (`/api/v1`, see the reference below): list, read and create tickets (on behalf of a person by e-mail, with an **Idempotency-Key** so a retried create returns the same ticket), add comments, list services. 120 requests a minute per key (`429` with `Retry-After` above).
4. **Webhooks**: an `https` address on the public internet, a description and events (`ticket.created`, `ticket.status_changed`, `ticket.assigned`, `ticket.commented` — public comments only, internal notes never leave — and `workorder.completed`). The signing secret (`whsec_…`) is **shown once** and kept encrypted (AES-GCM, like MFA seeds). Webhooks can be switched off or removed; each shows how many deliveries are waiting or failed, and its last 50 deliveries.
5. **Delivery**: events are written to an outbox **in the same transaction** as the change that caused them, then signed and posted by a dispatcher (several instances safe: `FOR UPDATE SKIP LOCKED`). Headers: `X-Annaba-Event`, `X-Annaba-Delivery` (the event id — the same for every endpoint, for replay protection), `X-Annaba-Timestamp`, `X-Annaba-Signature: v1=<hex HMAC-SHA256(secret, "{timestamp}.{body}")>`. Anything but 2xx is retried after 1 min, 5 min, 15 min, 1 h, 3 h, 6 h, 12 h (8 attempts, about a day), then given up. Delivered and given-up events are kept 30 days.
6. **SSRF protection**: webhook addresses must be `https` on port 443/8443, without user info, not an IP literal or name in a private, local or metadata range; and at **connect time** the host is resolved and every address must be public — so a name that points (or later re-points, DNS rebinding) at an internal address is never reached. No redirects are followed.

## Rules worth knowing
| Rule | Why |
|---|---|
| Keys act as their creator ∩ their scopes; only `/api/v1` endpoints marked with a scope accept them. | An allow-list: a new internal endpoint never becomes reachable by keys by accident. |
| Server-to-server calls (keys, inbound e-mail) skip the browser Origin check; they carry no cookie. | CSRF defends ambient browser credentials; a bearer key is not one. |
| Making a key or webhook, or pointing a webhook somewhere new, needs a recent password re-entry. | They hand the organisation's data to another system. |
| Rate limiting is per key and per API instance (in memory). | Simple and enough for one or two instances; noted as a gap. |
| Payloads carry ids and the ticket's main fields, not the full conversation; comment events carry the comment. | Least data that is useful; receivers call the API for more. |

## Public API reference (v1)
All calls: `Authorization: Bearer adk_…`, JSON. Errors use the usual envelope `{ error: { code, message, fields } }`.

| Call | Scope | Notes |
|---|---|---|
| `GET /api/v1/tickets?q&status&page&pageSize` | `tickets.read` | the tickets the key's person may see (staff queue rules); `status` as in the app (`open`, `submitted`, …) |
| `GET /api/v1/tickets/{id}` | `tickets.read` | the ticket with timeline, attachments list and SLA |
| `POST /api/v1/tickets` | `tickets.write` | `{ serviceId, subject, description?, requesterEmail?, locationId?, assetId?, answers? }`; header `Idempotency-Key` (≤ 100) makes repeats return the first ticket; `201` |
| `POST /api/v1/tickets/{id}/comments` | `tickets.write` | `{ body, internal? }` |
| `GET /api/v1/services` | `catalog.read` | the catalog's services |

Webhook verification (receiver side): compute `HMAC-SHA256(secret, timestamp + "." + rawBody)` in hex, compare with the value after `v1=` in constant time, reject timestamps older than 5 minutes, and ignore an `X-Annaba-Delivery` already processed.

## Tests added
**API — `IntegrationTests` (4):** only `integrations.manage` makes keys; scope validation; the token is never shown again (only its first characters); a create on behalf of a person with an Idempotency-Key returns the same ticket when repeated and is recorded as made by the key's owner; comment, list and read; a read-only key can't create or list services; a key is not a session outside `/api/v1` (`401`); a browser session can't use `/api/v1` (`401`); unknown and missing tokens `401`; a revoked key and an expired key stop at once; revocation is audited. The per-key rate limit answers `429`. Webhooks: unknown events refused; secrets shown once and never listed; a real receiver on 127.0.0.1 gets `ticket.created` and `ticket.commented` (not the internal note) with a valid signature and the event id; a switched-off endpoint gets nothing; a failing endpoint (`500`) shows one attempt, the status, and a next attempt a minute later in the delivery log. The SSRF rules (plain http, loopback, metadata, private, CGNAT, IPv6 local, `localhost`, single-label and `.internal` names, user info, odd ports, other schemes refused; public names and addresses accepted) and the back-off. **The public-endpoint guard test** is unchanged: the v1 API is not anonymous.
**Playwright — `integrations.spec.ts` (1) and `/admin/integrations` in the accessibility sweep (light and dark):** a key is made and shown once; another client without a session uses it on `/api/v1/tickets` through the web front, and can't create (no scope); a webhook is made (secret shown once), switched off, its empty delivery log opened, and removed (nothing is ever sent); the key is revoked and the other client is refused at once; the page passes the accessibility check.

## Decisions and known gaps
- **No OAuth client-credentials** and **no generated OpenAPI file** (the reference above is hand-written); a smaller, stable v1 surface first.
- **Webhook events are ticket and work-order events**; approvals, bookings and assets come with their modules (R3).
- **No manual re-send** of a given-up delivery yet; the receiver can read the current state through the API.
- Rate limits are in memory per instance (not shared between instances).
- Test environments allow webhooks to local addresses (`Webhooks:AllowPrivateTargets`); production must leave it off (the default).

## Verification of the functional behaviour
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16, Testcontainers) | **638 passed, 0 failed** (632 before slices 9–10; +2 `EmailIntakeTests`, +4 `IntegrationTests`) |
| `tsc --noEmit`, `eslint`, `next build` | pass (one pre-existing `<img>` warning in the app icon) |
| Full Playwright suite (light + dark accessibility, responsive) | **222 passed, 0 failed** (216 before slices 9–10; +1 e-mail to ticket, +1 integrations, +4 accessibility pages) |

## Found and fixed while testing
- The first full API run had 56 failures from "too many clients": the test database server ran out of connections, because every isolated test host keeps its own pool and background pollers alive for the whole run, and this slice adds one more poller. The test container now allows 400 connections and isolated hosts use small pools that release idle connections; then all 638 passed. Production is unaffected (one host, one pool).

## Next
R2-11: vendor portal — vendor sign-ins limited to their own work orders.
