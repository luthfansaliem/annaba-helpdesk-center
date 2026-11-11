# 65 — GA Helpdesk R3 slice 10: WhatsApp, Slack and Microsoft Teams

Status: **implemented, verification below**. Tenth slice of R3 (`55-ga-helpdesk-r3-plan.md`). It covers NTF-003, the outside notification channels.

## What was done
1. **The organisation's set-up** (*Administration → Notification channels*, `integrations.manage`).
   - **WhatsApp** (Meta Cloud API): phone number ID, access token, a notification template with three body variables (title, text, link), a code template with one variable, and an optional template language.
   - **Slack:** a bot token with the scopes `chat:write` and `users:read.email`.
   - **Microsoft Teams:** nothing to set up. Each person connects their own chat with a Workflows flow.
   - A channel can only be turned on when what it needs is filled in.
   - Credentials are encrypted with the app's secret key and bound to the row. They are never returned: the screen shows "Stored — type to replace".
   - Every change is audited (`channel.settings`, recording whether credentials changed, never their value).
2. **Each person opts in** (*Notifications → Settings → Other channels*).
   - **WhatsApp:** a mobile number in local or international form (0812…, +62…, normalised to E.164). A six-digit code is sent over WhatsApp and must be confirmed within 10 minutes; five wrong codes end it.
   - **Slack:** found by the person's own e-mail in the organisation's workspace.
   - **Teams:** the HTTPS address of their Workflows flow ("Post to a chat when a webhook request is received"). Only Microsoft Workflows and connector hosts are accepted.
   - Addresses (phone number, Slack member ID, Teams URL) are personal data and are encrypted. The screen shows a hint such as `+62 •••• 7890`.
   - Per channel: on or paused, *instead of e-mail*, a test message, disconnect.
3. **Behind the notification port.**
   - Whatever would be e-mailed to a person (their notification settings decide) also goes to each channel they confirmed, if the organisation still offers it.
   - With *instead of e-mail*, the e-mail is held back. When every channel that replaced it has given up, the e-mail goes after all (`channel.fallback`).
4. **Delivery.**
   - An outbox (`channel_deliveries`) is sent every 5 seconds by a worker; each delivery is claimed by a conditional update, so two instances never send it twice.
   - Retries back off from 30 s to 1 h, up to 5 attempts. A refusal that will not get better (bad token, unknown recipient, 4xx) stops at once.
   - The message text is cleared once delivered or given up. The log keeps who, which channel, what kind, attempts and result for 30 days.
   - The person sees the last error on their channel.
   - All calls go through the SSRF-guarded HTTP client used for webhooks, because the Teams address is typed by a person.
5. **Audit:** `channel.settings`, `channel.code_sent`, `channel.connect`, `channel.disconnect`, `channel.fallback`.

## Decisions
- **Mirror e-mail, don't add a third set of switches.** People already choose per notification whether it e-mails them (NTF-002). Channels follow that choice, which keeps the settings page understandable. The master *e-mail notifications* switch only governs e-mail.
- **Teams through the person's own Workflows flow.** Personal Teams messages otherwise need a registered bot and Graph permissions that an organisation must approve. The Workflows template is self-service and documented by Microsoft. Organisations that want a bot can add it later behind the same port.
- **WhatsApp templates only.** Outside a 24-hour customer window, Meta only delivers approved templates. The organisation names its approved templates; the helpdesk never sends free text there.
- **Credentials per organisation, not in configuration.** Each organisation brings its own WhatsApp number and Slack workspace, so nothing goes into the environment variables.

## Tests added
- **API, `ChannelTests` (1, an isolated organisation; WhatsApp, Slack and Teams replaced by a recording stand-in):**
  - **Set-up:**
    - Only `integrations.manage` sees the set-up.
    - WhatsApp cannot be turned on without its phone number ID and token.
    - The token is not in the API answer and not readable in the stored bytes.
  - **Opting in:**
    - WhatsApp: "0812-3456-7890" becomes `+6281234567890` and shows as `+62 •••• 7890`. The code goes out with the code template and the stored token; a wrong code is refused and the right one confirms.
    - Slack: not found until the workspace has the person's e-mail, then connected to their member ID.
    - Teams: a foreign host is refused, a `logic.azure.com` flow is accepted.
  - **Delivering:**
    - Logging incoming mail for the person reaches all three channels, with the link to the mail page, and still by e-mail.
    - With WhatsApp *instead of e-mail* and WhatsApp failing:
      - the e-mail is held back;
      - the delivery is retried, and not again before its back-off;
      - after 5 attempts it gives up, its text is cleared and the e-mail is sent after all;
      - the person sees "HTTP 503".
    - The delivery log shows the fallback, and the test message arrives.
    - Turning WhatsApp off removes it from the person's choices and refuses tests.
- **Playwright, `channels.spec.ts`:**
  - WhatsApp refuses to turn on without its settings.
  - The admin offers Teams.
  - The member's foreign URL is refused, then their Workflows URL is connected and disconnected again.
  - Both pages pass the accessibility check.

## Known gaps
- **No inbound replies.** Answering a WhatsApp or Slack message does not comment on the ticket; that needs webhooks from each platform.
- **No Teams bot** and no channel posts for teams (only personal chats).
- **One WhatsApp number per organisation**, and no per-site sender.

## Found and fixed during verification
- **Slack could never be connected.** The e-mail lookup was given the public settings without the bot token. `ChannelTests` caught it, and the lookup now gets the decrypted credentials.
- **Error text under form fields failed WCAG contrast app-wide.** `.field-error` was #ef4444 on white (about 3.8:1). It now uses `--red-ink` (#b42318, about 6.5:1; dark mode #ff948c). The accessibility check on the channel page found it because that page shows field errors while being checked.
- **A booking test depended on the time of day.** `BookingLifecycleTests` checked weekdays by their UTC date while the server counts them in the room's time zone (UTC+7). It failed once in a full run and passed on every rerun. It now compares in UTC+7 and passed in the clean full run.

## Verification
| Check | Result |
|---|---|
| `tsc --noEmit`, `eslint` | pass |
| `next build` | pass |
| `dotnet test api/AnnabaDigital.sln` | **670 passed, 0 failed** (clean full run of all of R3) |
| Full Playwright suite | **236 passed, 0 failed** (clean full run of all of R3) |
