# Hardening report — durable e-mail outbox and wider e2e coverage

Follows slice 7 (`20-phase4-slice7-report.md`), which listed these as known gaps.

## 1. Durable e-mail outbox (replaces the in-memory queue)
- `IEmailQueue.Enqueue` now writes a row to `outbox_emails` (migration `OutboxEmails`) using its own short-lived context, so it works after the caller's transaction has committed (every call site enqueues post-commit). A restart or crash no longer loses mail.
- `EmailDispatcherService` claims due rows with `UPDATE … WHERE id IN (SELECT … FOR UPDATE SKIP LOCKED) RETURNING *`, so several API instances can run safely. A claim bumps `attempts` and leases the row for 2 minutes; a worker that dies mid-send is retried after the lease and the attempt still counts.
- Failures back off exponentially (6 s, 18 s, … capped at 5 min) up to `Email:MaxAttempts` (default 3), then the row is marked failed and never retried.
- **Security property:** the body contains one-time links, so `text`/`html` are wiped the moment a message is sent or abandoned. Only delivery metadata remains, and `last_error` stores the exception type only (never a provider response). Rows past 7 days are purged hourly.
- Tests: the old channel test is replaced by 4 Postgres-backed tests (send and wipe, backoff then success, give up without leaking details, lease expiry and restart). `dotnet test`: 260 passed.

## 2. New e2e journeys (`e2e/hardening.spec.ts`)
Recovery-code sign-in (second use is rejected), bulk suspend (confirmation, all selected users signed out), role duplicate then archive, and a revoked session landing on `/login?returnTo=…`.
Each test uses its own client IP via `X-Forwarded-For`, because the API throttles invite/login per IP and a single e2e run would otherwise exhaust it.

## 3. Defect found
The bell badge (`.bell b`) failed axe colour contrast once the admin had notifications (white on coral). Now `#b42318`. This only showed up after new tests generated data, which is a reminder that axe results depend on page state.

## Verification
`dotnet test` 260 · vitest 28 · Playwright 55 · `tsc`, `next lint`, `next build` clean.

## Still not covered
Avatar upload, the Resend adapter against the real service, an HIBP outage through the UI, multi-tab CSRF, the PWA offline path against the real API, keyboard-only and screen-reader passes, browsers other than Chromium. The `outbox_emails` rows are not yet visible in any admin screen (no "failed e-mails" view or alert).
