# 34 — GA Helpdesk, R1 slice 11: notification preferences

Status: **implemented and verified** (API against PostgreSQL, browser flow and accessibility in Chromium). Part of release R1 (see `23-ga-helpdesk-prd.md`). Requirement covered: **NTF-002** (per-user preferences). **NTF-004** (delivery tracking, retry, dead letters) was reviewed and is largely already in place — see below. Per-tenant editable templates, also in NTF-002, are **not** built.

## Why this slice
Until now a person had one switch: "e-mail notifications" on or off. They could not say "tell me in the app when work starts but never e-mail me", or "e-mail me when something is put on hold". Agents who work many tickets and requesters who file a few need opposite settings. The notification system already knew fifteen distinct kinds of events; this slice lets each person decide, per kind, what appears in the app and what is also e-mailed.

## What was built
| Area | Where |
|---|---|
| `NotificationCatalog`: the fifteen kinds, their group (tickets, service levels, approvals), whether e-mail is on by default, and which kind is locked | `api/src/AnnabaDigital.Domain/NotificationCatalog.cs` |
| `notification_preferences` table (migration `NotificationPreferences`): only choices that differ from the default are stored | `Domain/Entities/Approvals.cs`, `AnnabaDigitalDbContext.cs` |
| `NotificationService.SendAsync` honours the choices; `PreferencesAsync`, `SetPreferencesAsync`, `ResetPreferencesAsync` | `Application/Helpdesk/NotificationService.cs` |
| A generic bilingual e-mail for kinds that have no message of their own, so a person can opt in to e-mail for any event | `EmailTemplates.Notification` |
| `GET/PUT /api/notifications/preferences`, `POST …/reset` | `ApprovalEndpoints.cs` |
| UI: Notifications → **Settings**: a table per group with *In the app* and *By e-mail* boxes, save and "back to the usual settings" | `src/app/(app)/notifications/settings/page.tsx` |

## How it behaves
- **Defaults are what the system did before**, so nothing changes for anyone who does nothing: everything in the app; e-mail only for *assigned to you*, *a reply*, *resolved*, *SLA breached*, and the approval messages (needed, approved, rejected).
- **In the app** can be switched off for any kind except the locked one.
- **E-mail** can be switched off for kinds that are mailed, and **switched on for kinds that are not** (work started, on hold, closed, cancelled, reopened, internal note, SLA at risk, cover requests). Those arrive as a plain message with the notification's own words and a link, in the person's language (English or Bahasa Indonesia).
- **The profile's e-mail switch still wins.** If a person has e-mail off in their profile, nothing is mailed except the locked kind, whatever they chose per kind; the settings page says so and links to the profile.
- **Locked: "approval needed".** It stays on in the app and by e-mail, because the request waits for the answer; the API refuses to switch it off and the boxes are shown ticked and disabled.
- **Only differences are stored.** A choice equal to the default is stored as "as usual" (or the row is removed), so a later change of default reaches everybody who never touched that kind, and "back to the usual settings" simply deletes the rows.
- Nobody is notified about their own action (unchanged); choices are per person and cannot be read or changed by anyone else; every real change is audited (`notification.prefs`).

## NTF-004 (delivery tracking) — what already exists
Slices before this one already built the outbox with retry and back-off (`outbox_emails`: attempts, next attempt, failed-at, last error type) and an administrator's read-only **E-mail deliveries** screen (queued / sent / failed, counts for the last 24 hours and 7 days), with bodies wiped once sent or abandoned. What the requirement still lists and is **not** built: re-sending a failed message (the body is deliberately deleted when a message is abandoned, because it may hold a one-time link, so a retry would need the message re-created by the original event), a dead-letter alert, and unsubscribe links (not legally required for transactional mail of this kind).

## Verification
| Check | Result |
|---|---|
| `dotnet test api/AnnabaDigital.sln` (PostgreSQL 16, Testcontainers) | **574 passed, 0 failed** |
| `NotificationPreferenceTests` (5): the catalog and defaults for every kind, anonymous refused; a kind switched off in the app disappears for that person only while other kinds and other people are unaffected; e-mail opt-in for a kind with no e-mail of its own (the plain message arrives) and opt-out for a kind that is normally mailed (still in the app, no mail — checked after a later mail proved the outbox had run); the profile switch silences everything but the approval request; locked, unknown, duplicate, empty and oversized requests refused, defaults not stored, changes audited, reset clears every row | pass |
| `tsc --noEmit`, `eslint`, `next build` | pass (one existing `<img>` warning) |
| Playwright `notification-settings.spec.ts`: reach the page from Notifications, the usual state, the locked boxes disabled, nothing to save until something changes, save → reload keeps it, reset restores the usual; axe | **1 passed** |
| Full Playwright suite incl. axe light + dark and the responsive checks for the new page | **161 passed, 0 failed** |

## Decisions and known gaps
- **No digest** (a daily summary instead of single e-mails) and **no quiet hours**: the catalog and the stored rows are ready for a third setting, but a digest needs a scheduled job and a summary template.
- **Per-tenant templates (NTF-002) are not built.** The e-mail wording is still code (English and Bahasa Indonesia); an organisation cannot edit it or its variables. That is a larger feature (storage, a safe variable language, preview, versioning) and is the natural next step if organisations ask to change the wording.
- The in-app text of a notification is stored in English at the moment it is created; only e-mails are localised. Localising the in-app text means storing a key and parameters instead of the finished sentence.
- Notifications about a person's own action are never sent, regardless of settings.
- Agents' replies to a requester are mailed by default, but a requester's reply to the assignee is in the app only (as before); an agent who wants it by e-mail ticks the *Someone replies on a ticket* box.

## Next
Per-tenant e-mail wording (and in-app notification texts by key, so they can be localised), a digest option, and then the pilot checklist from `23-ga-helpdesk-prd.md` §14 (privacy and retention policy for ticket data and files, load test, in-app help); or release R2.
