# 39 — GA Helpdesk, R1 slice 16: per-organisation e-mail wording

Status: **implemented and verified** (API and Playwright suites on the final code; see the table at the end). Part of release R1 (`23-ga-helpdesk-prd.md`): the per-tenant part of **NTF-002** and the last open item of the §14 pilot checklist. Slice 11 delivered the per-person part (preferences by kind and channel).

## What was done
1. **Each organisation can make its e-mails its own** (*Administration → Email Wording*, permission `settings.manage`):
   - a **name in the sign-off** (replaces "— Annaba Digital"), up to 60 characters;
   - a **start of every subject**, for example `[Acme GA]`, so people can recognise and filter the messages, up to 30 characters;
   - a **closing paragraph in English and in Bahasa Indonesia**, for example who to call when it is urgent, up to 400 characters on 6 lines.
2. **Applied to every mail of that organisation in one place**, where the outbox queues a message, after the message is put into the recipient's language: helpdesk mails (assigned, reply, resolved, approval needed or decided, SLA breached, notification copies) and account mails (invitations, password, security notices). If only one closing text is written it is used for both languages.
3. **A preview** shows a typical message (a resolved request) in both languages with the text as typed, saved or not; nothing is saved or sent.
4. **Use the standard wording** clears all four boxes.

## Rules
| Rule | Why |
|---|---|
| Plain text only. It is HTML-escaped in the HTML part and kept as is in the text part. | An administrator's text must never become markup or a disguised link in someone's mail. |
| The name and the subject start are one line with no control characters; the closing texts may have line breaks (at most 6 lines). | A line break in a subject is a header-injection risk; anything else is just text. |
| It only **adds** to the product's messages (prefix, closing paragraph, sign-off name). The product's own sentences, the one-time links and their expiry wording are never changed. | Security messages (password reset, approval links) keep their exact meaning, and a translation is never half-replaced. |
| Saving needs a fresh password check, is audited (`settings.email_wording.update`, before/after per box) and an unchanged save is refused. | It changes what every person in the organisation receives. |
| Each organisation has its own row; the mail of one organisation never carries another's wording. The platform organisation can set its own too. | Multi-tenancy. |

## How it is built
- Table **`email_wordings`** (one row per organisation; migration `EmailWording`); entity and validation in `Domain/Entities/EmailWording.cs`.
- `EmailWordingService` (Application): read, save, preview, and the pure `Apply(message, wording, language)` used by `OutboxEmailQueue.Enqueue`. The recipient's language is now looked up for every message (it was only looked up for messages that had a translation).
- Endpoints: `GET/PUT /api/settings/email-wording`, `POST /api/settings/email-wording/preview` (`08-api-spec.md` §12.9).

## Tests added
**API — `EmailWordingTests` (6, each in its own database):** permissions and the fresh password check; validation (length, line breaks, control characters), trimming, "blank means none", audit and the refused unchanged save, going back to the standard; a real resolved-request mail before and after (prefix, closing text, sign-off name, escaped HTML, link untouched), the same mail in Indonesian for a person who chose Indonesian, the one-language fallback, and an invitation carrying it with its token intact; another organisation keeps the standard and can set its own; the preview in both languages saves and sends nothing; `Apply` leaves a message alone without wording and still works on a message without the usual sign-off.
**Playwright — `email-wording.spec.ts` (2) and the page in the accessibility sweep (light and dark):** write, preview in both languages, save, reload, back to the standard (and always restored afterwards, because other specs read real mail); people without the permission do not see the page.

## Decisions and known gaps
- **Not full templates.** NTF-002 in the PRD also mentions per-tenant templates with variables and a digest option. This slice gives each organisation its own voice at the edges of every message; rewriting the body of each of the ~30 message kinds per organisation (with variables) is **not done**, and neither is a **daily digest**. Both remain open under NTF-002 for R2. The edges-only approach was chosen because a free body template can break the meaning of security and approval messages and doubles the translation work per organisation.
- **The sender address and display name** (`From:`) stay the platform's setting; a per-organisation sender needs per-organisation verified domains at the mail provider.
- **No logo or colours** in the HTML.
- **Mail already in the outbox** keeps the wording it was queued with.

## Verification of the functional behaviour
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16, Testcontainers) | **595 passed, 0 failed** (589 before; +6 `EmailWordingTests`) |
| `tsc --noEmit`, `eslint`, `next build` | pass (one pre-existing `<img>` warning in the app icon) |
| Full Playwright suite (light + dark accessibility, responsive) | **176 passed, 0 failed** (172 before; +2 e-mail wording, +2 accessibility pages) |

## Where R1 stands
With this slice every item of the §14 pilot checklist that can be built has been built (retention, restore drill, in-app help, per-tenant wording). What is left before a pilot is outside the code: the deploy of slices 13–16 to staging (backup service change, migrations `DataRetention` and `EmailWording`), a load test on the real hardware, a native-speaker read of the Indonesian help, and a privacy/legal review. Next in the product: release R2 (work orders, assets, preventive maintenance), plus the open NTF-002 parts above.
