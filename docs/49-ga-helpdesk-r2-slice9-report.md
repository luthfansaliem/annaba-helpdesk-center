# 49 — GA Helpdesk, R2 slice 9: e-mail to ticket

Status: **implemented and verified** (API and Playwright suites on the final code, run together with slice 10; see the table at the end). Ninth slice of release R2 (`40-ga-helpdesk-r2-plan.md`): the e-mail part of **TKT-004** ("comments by e-mail reply") and new requests by e-mail.

## What was done
1. **A secret webhook for the organisation's helpdesk address** (*Administration → Email to Ticket*). The organisation's mail service (Mailgun, Postmark, SendGrid Inbound Parse, a Cloudflare Email Worker…) posts every e-mail sent to, say, `help@company.co.id` as the raw message to a secret address. The address is made on the page (after re-entering the password), **shown once**, and can be replaced at any time; only a hash of its token is stored.
2. **New e-mails become requests** on the service chosen for the mailbox, filed as the sender: the subject without "Re:/Fwd:", the text as the description, attachments with the usual file checks (type list, 8 MB, virus scan). Routing, SLA and notifications work as for any request.
3. **Replies become comments.** When the subject carries a ticket number — as the helpdesk's own e-mails do ("New reply on GA-2026-000123") — and the sender can see that ticket (its requester, or staff of its team), only the **new text** is added as a public comment marked "(by e-mail)": the quoted earlier mail ("On … wrote:", "Pada … menulis:", "-----Original Message-----", "From:" headers, `>` lines) and the signature are left out. Someone who cannot see the ticket starts a request of their own instead; nothing reaches the ticket.
4. **Loop and abuse protection.** Only active people of the organisation are heard (the sender must be a sign-in e-mail); auto-replies, bounces, mailing lists and no-reply senders are ignored (no answer, so no mail loops); the same Message-ID (a provider's retry) is not filed twice; at most 20 messages per sender and 500 per mailbox an hour; 10 MB per message. Replies to finished tickets are refused and the sender is told in the app.
5. **A log of every message** on the settings page: when, from whom, the subject, and what became of it (*New request*, *Reply added*, *Ignored*, *Refused* with the reason), linking to the ticket.

## Rules worth knowing
| Rule | Why |
|---|---|
| The webhook is anonymous on purpose: the token in its URL is the credential (like the approval links). It is added to the guarded list of public endpoints. | Mail services cannot sign in. |
| Server-to-server endpoints are marked so the browser-only Origin check does not apply to them; they never act on a cookie session. | The Origin check protects browsers; a mail service has no Origin. The same marker serves the API keys of R2-10. |
| The sender address is trusted as the mail service delivers it. | Spoofing protection (SPF, DKIM, DMARC) is the mail service's job; configure it to reject failing mail. Stated in the runbook. |
| Unknown senders are refused, never given an account. | No account creation from the outside. |
| Replies only count when the sender can already see the ticket. | An e-mail cannot be used to read or write someone else's ticket. |

## Tests added
**API — `EmailIntakeTests` (2):** settings validation (a published service; no switching on without a token; the token needs a recent password re-entry; only settings managers); an unknown token and a switched-off mailbox answer `404`; a mail posted without Origin or session becomes a request filed for the sender (sender address matched case-insensitively), on the right team, with "Fwd:" removed and its picture attached; the provider's retry is a duplicate; a reply from the requester and one from the team's agent add only their new text (quote and signature removed); a person who cannot see the ticket gets a new request instead; an unknown sender is refused, an auto-reply and a quote-only reply are ignored, a reply to a cancelled ticket is refused and the sender told in the app; something that is not an e-mail is `422`; the log lists every outcome. And the pure rules: new text in English and Indonesian reply styles, Outlook's "Original Message", quoted lines and signatures; ticket numbers in subjects; "Re:/FW:" removal; automatic-mail headers and senders. **The public-endpoint guard test** now lists the webhook.
**Playwright — `inbound-email.spec.ts` (1) and `/admin/inbound-email` in the accessibility sweep (light and dark):** the admin makes the webhook address (shown once), sets the address and service and switches the mailbox on; a separate client with no session posts a raw e-mail to that URL **through the web front** (as a mail service would in production); it becomes a request; the log shows *New request* with the ticket link; the requester sees the request under *My requests*; the page passes the accessibility check.

## Decisions and known gaps
- **No IMAP/POP polling**: the mailbox works through the mail service's inbound webhook. Polling an IMAP box needs stored mail-server passwords and a mail server to test against; it can be added behind the same intake if a customer cannot use a webhook.
- **One mailbox per organisation**, one service for new e-mails (no routing by address or keywords yet; assignment rules still apply after creation).
- **No HTML formatting** is kept in comments (plain text; HTML-only mail is converted to text).
- **Our outgoing e-mails do not set a Reply-To** to the helpdesk address; people reply to whatever the sender address of the helpdesk's e-mails is, so the outbound "from" address should be the same helpdesk address (configure in *Email Delivery*).
- Limits are fixed (10 MB, 10 attachments, 20 per sender and 500 per mailbox an hour, 4000 characters).

## Verification of the functional behaviour
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16, Testcontainers) | **638 passed, 0 failed** — run together with slice 10 (632 before; +2 `EmailIntakeTests`, +4 `IntegrationTests`) |
| `tsc --noEmit`, `eslint`, `next build` | pass (one pre-existing `<img>` warning in the app icon) |
| Full Playwright suite (light + dark accessibility, responsive) | **222 passed, 0 failed** — run together with slice 10 (216 before; +1 e-mail to ticket, +1 integrations, +4 accessibility pages) |

## Next
R2-10: API keys and webhooks — per-organisation keys (stored hashed) with scopes, and signed, retried webhooks for ticket events with SSRF-safe delivery.
