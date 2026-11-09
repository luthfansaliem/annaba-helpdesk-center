# 03 — Information Architecture & Screen Inventory

## 1. Sidebar hierarchy (authenticated shell)
```
Dashboard                                  /dashboard              (landing after login)
Identity & Access
  Users ▸ Overview                         /users                  users.read
        ▸ All Users                        /users/all              users.read      [+ Add User button in header]
        ▸ Invitations                      /users/invitations      invitations.read
  Roles & Permissions ▸ Roles              /roles                  roles.read
                      ▸ Permissions        /permissions            permissions.read
                      ▸ Access Matrix      /access-matrix          permissions.read (+assign to edit)
  Sessions & Devices                       /sessions               own sessions always; all with sessions.read_all
Monitoring ▸ Audit Logs                    /audit                  audit.read
           ▸ Security Events               /security-events        security.read
Personal   ▸ My Profile                    /profile                authenticated
           ▸ Settings                      /settings               authenticated
Administration ▸ System Settings           /admin/settings         settings.manage
```
Rules: items are **filtered by permission** (UI) *and* enforced on server. **No "Add User" item.** Active group auto-expands. Desktop collapse → compact icon rail with tooltips. Mobile → drawer.

## 2. Route map
| Area | Route | Notes |
|---|---|---|
| Public auth | `/login`, `/register`, `/verify-email`, `/forgot-password`, `/reset-password`, `/mfa`, `/locked`, `/invite/[token]` | no shell |
| Status pages | `/account/pending`, `/account/suspended`, `/account/deactivated`, `/account/locked`, `/session-expired`, `/403`, `/invite/expired` | recovery actions on each |
| App | routes in §1 plus `/users/[id]` (tabs via `?tab=`), `/roles/[id]`, `/audit/[id]` | |
| API | `/api/auth/*`, `/api/users`, `/api/invitations`, `/api/roles`, `/api/permissions`, `/api/sessions`, `/api/audit`, `/api/security-events`, `/api/settings` (contracts in Phase 3) | Server Actions where form-bound |

## 3. Screen inventory × states
States: **L**oading · **E**mpty · **P**opulated · **V**alidation error · **S**erver error · **D**enied · **OK** success · **C**onfirm · **X** expired session. ● = required.

| # | Screen | L | E | P | V | S | D | OK | C | X |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Login | | | ● | ● | ● | | ● | | ● |
| 2 | MFA challenge | | | ● | ● | ● | | ● | | ● |
| 3 | Register | | | ● | ● | ● | ● (closed mode) | ● | | |
| 4 | Verify email | ● | | | | ● | | ● | | ● (expired token) |
| 5 | Forgot password | | | ● | ● | ● | | ● | | |
| 6 | Reset password | ● | | ● | ● | ● | | ● | | ● |
| 7 | Lock screen | | | ● | ● | ● | | ● | | ● |
| 8 | Account state pages (5) | | | ● | | | | | | |
| 9 | Invitation accept | ● | | ● | ● | ● | | ● | | ● (expired) |
| 10 | Dashboard (admin) | ● | ● | ● | | ● | ● | | | ● |
| 11 | Dashboard (user) | ● | ● | ● | | ● | | | | ● |
| 12 | Users overview | ● | ● | ● | | ● | ● | | | |
| 13 | All Users (+ Add User) | ● | ● | ● | ● | ● | ● | ● | ● | ● |
| 14 | Add User drawer | | | ● | ● | ● | ● | ● | | |
| 15 | User detail (6 tabs) | ● | | ● | ● | ● | ● | ● | ● | |
| 16 | Invitations | ● | ● | ● | ● | ● | ● | ● | ● | |
| 17 | Roles list / role editor | ● | ● | ● | ● | ● | ● | ● | ● | |
| 18 | Permissions catalog | ● | | ● | | ● | ● | | | |
| 19 | Access Matrix | ● | | ● | | ● | ● | ● | ● (save diff) | |
| 20 | Sessions & Devices | ● | ● | ● | | ● | | ● | ● | |
| 21 | Audit logs + detail | ● | ● | ● | | ● | ● | | | |
| 22 | Security events | ● | ● | ● | | ● | ● | ● (ack) | | |
| 23 | My Profile (tabs: profile, security, MFA, sessions, activity, notifications) | ● | | ● | ● | ● | | ● | ● | ● |
| 24 | Personal settings | ● | | ● | ● | ● | | ● | | |
| 25 | System settings | ● | | ● | ● | ● | ● | ● | ● | |
| 26 | 403 / 404 / 500 | | | ● | | | | | | |

## 4. Shell behaviours (acceptance checklist)
- [ ] Active group auto-expands on navigation; manual toggle works
- [ ] Desktop collapse ↔ compact rail; persisted; tooltips; keyboard operable
- [ ] Mobile drawer: open button, X, backdrop, Esc, closes on selection; focus trap + restore
- [ ] Long nav lists scroll inside sidebar without clipping footer controls
- [ ] Single collapse/hide control (no duplicate "Hide Menu")
- [ ] Verified at 1440 / 1366 / 1024 / 768 / 390 / 375

## 5. Reusable component gaps to build in Phase 2/4
`Dialog`/`Drawer` (focus trap, Esc), `ServerDataTable` (sort/paging/loading/error), `StatusBadge` set, `PasswordStrengthMeter`, `OtpInput`, `AccessMatrix`, `PermissionGate`, `Skeleton`, `ErrorState`, `AccessDenied`, `Breadcrumbs`, `Stepper`, `CopyField`, `DiffViewer`.
