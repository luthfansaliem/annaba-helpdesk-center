# 02 — End-to-End User Flows

Legend: ✔ happy path · ✖ failure/edge. All authorization decisions occur server-side.

## F1 Login (AUTH-001/007/008)
1. Visit protected URL → ✖ unauthenticated → redirect `/login?returnTo=<path>` (only same-origin relative paths accepted; otherwise `/dashboard`).
2. Submit email+password → rate-limit check (IP + account) → verify Argon2id (dummy hash when user unknown → same timing).
3. ✖ bad credentials: generic "Email or password is incorrect"; record failed attempt; after N failures → temporary lock (security event) with retry-after notice.
4. ✖ account state: pending verification → *Verify your email* screen (resend); suspended/deactivated → *Account unavailable* (contact admin); locked → *Temporarily locked*.
5. If MFA enrolled (or policy requires) → challenge screen (TOTP or recovery code) → ✖ wrong code counts toward lockout. If policy requires MFA and not enrolled → forced enrollment.
6. ✔ create session (rotated token, cookie), audit `auth.login.success`, redirect to `returnTo` or role-appropriate dashboard.

## F2 Registration & verification (AUTH-002/003)
Registration disabled in invitation-only mode (page shows "Invitation required"). When enabled: submit → policy validation → create `pending` user (or, if email exists, silently send "you already have an account" email) → always show "Check your inbox". Verify link → token valid ✔ activate → login; ✖ expired/used → *Link expired* with resend (rate-limited).

## F3 Forgot / reset password (AUTH-004/005)
Email → always "If an account exists, we sent instructions". Link → valid token → new password (policy) → consume token, revoke all sessions (policy), audit + security event, redirect `/login` with success toast. ✖ invalid/expired → request new link.

## F4 Lock screen (AUTH-006)
User clicks *Lock* → server marks session `locked_at` → UI routes to `/locked`. Any API/page request with a locked session returns 423/redirect. Unlock: password (+TOTP if MFA) → clears lock, audit. ✖ N failed unlocks → session revoked & sign-in required. Idle auto-lock optional (policy).

## F5 MFA (AUTH-007)
Enroll: reauth → generate secret (encrypted) → QR + manual key → confirm 6-digit code → show 10 recovery codes once → enabled. Challenge on login. Regenerate codes: reauth → old codes invalidated. Disable: reauth (+code) → secret/codes deleted, security event.

## F6 Admin creates user (USR-003)
All Users → **+ Add User** (top-right) → drawer: name, email, roles, mode {Send invitation | Create active user (verification email, no password)}. Validate → duplicate check → authorization (`users.create` + `users.assign_role`, and cannot assign roles with permissions caller lacks) → transaction (user, role assignments, invitation/verification token, audit) → success toast + row appears; ✖ server errors shown inline.

## F7 Invitation lifecycle (INV-001..003)
Create → email with single-use link → Pending. Invitee opens link → ✔ set password → account activated, roles applied, marked Accepted. ✖ expired → *Invitation expired* (ask admin); admin can Resend (new token, old invalid) or Revoke.

## F8 User administration (USR-005)
Detail page tabs. Suspend/Deactivate/Revoke sessions/Remove role → confirmation dialog stating impact → server action → audit. ✖ removing last `super_admin` or suspending self → blocked with explanation.

## F9 Role & permission change (ROLE/PERM)
Access Matrix → toggle cells (dirty indicator) → Save → confirm dialog lists diff → transaction + audit (before/after). Caller cannot grant a permission they do not hold; protected roles read-only. Affected users' next request recalculates effective permissions (no stale grants).

## F10 Sessions (SES-001)
My Profile / Sessions & Devices → list → Revoke one (not current without confirmation) / Revoke all others / Sign out everywhere. Admin with `sessions.revoke` can revoke another user's sessions.

## F11 Expired / denied
Idle or absolute expiry → *Session expired* with sign-in + preserved `returnTo`. Missing permission → 403 *Access denied* page (with request-access hint), never a blank screen.

## F12 Profile/security changes (PRF-001/002)
Sensitive changes (email, password, MFA) require recent reauth (≤5 min). Email change: verify new address before switch; notify old address.
