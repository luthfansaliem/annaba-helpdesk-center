# 09 — RBAC Permission Matrix (Phase 3)

Status: **proposed — awaiting PO approval.** Mirrors the prototype's seed (`src/proto/data.ts`). ● = granted, — = not granted. `super_admin` is protected: its permission set cannot be changed.

## 1. Seed roles × permissions
| Module | Permission | Critical | Super Admin | User Admin | Security Auditor | User | HR Manager *(custom example)* |
|---|---|:-:|:-:|:-:|:-:|:-:|:-:|
| Users | `users.read` | | ● | ● | ● | — | ● |
| Users | `users.create` | | ● | ● | — | — | — |
| Users | `users.update` | | ● | ● | — | — | — |
| Users | `users.suspend` | | ● | ● | — | — | — |
| Users | `users.assign_role` | ✔ | ● | ● | — | — | — |
| Invitations | `invitations.read` | | ● | ● | — | — | ● |
| Invitations | `invitations.manage` | | ● | ● | — | — | ● |
| Roles | `roles.read` | | ● | ● | ● | — | — |
| Roles | `roles.create` | | ● | — | — | — | — |
| Roles | `roles.update` | | ● | — | — | — | — |
| Permissions | `permissions.read` | | ● | — | ● | — | — |
| Permissions | `permissions.assign` | ✔ | ● | — | — | — | — |
| Sessions | `sessions.read_all` | | ● | ● | ● | — | — |
| Sessions | `sessions.revoke` | | ● | ● | — | — | — |
| Monitoring | `audit.read` | | ● | — | ● | — | — |
| Monitoring | `security.read` | | ● | — | ● | — | — |
| Administration | `settings.manage` | ✔ | ● | — | — | — | — |

Self-service (no permission, always limited to own data): profile, avatar, password/email change, MFA, own sessions, own activity, preferences, lock/unlock.

## 2. Route → permission (UI nav & API)
| Screen | Permission to view | Extra for actions |
|---|---|---|
| Dashboard (admin widgets) | any of `users.read`, `audit.read`, `security.read`, `settings.manage`; each widget gated separately | — |
| Users overview / All Users / User detail | `users.read` | Add `users.create`+`users.assign_role`; Edit `users.update`; Suspend/Activate/Unlock `users.suspend`; Roles tab edit `users.assign_role`; Revoke sessions `sessions.revoke` |
| Invitations | `invitations.read` | create/resend/revoke `invitations.manage` |
| Roles | `roles.read` | create/duplicate `roles.create`; edit/archive `roles.update`; change permissions `permissions.assign` |
| Permissions | `permissions.read` | — |
| Access Matrix | `permissions.read` | save `permissions.assign` |
| Sessions & Devices | authenticated | other users' list `sessions.read_all`; revoke others `sessions.revoke` |
| Audit Logs | `audit.read` | — |
| Security Events | `security.read` | acknowledge: `security.read` (candidate for its own `security.review`) |
| My Profile / Settings | authenticated | sensitive changes need recent reauth |
| System Settings | `settings.manage` | — |

## 3. Authorization invariants (each has a test — see test plan)
1. **Deny by default**; unknown permission/route ⇒ 403.
2. **No self-escalation / no escalation by delegation:** an actor can grant a role/permission/invitation role only if they hold **every** permission it contains (`grantableRoles`).
3. **Protected roles** (`super_admin`) cannot be edited, archived or have members removed such that no active Super Admin remains.
4. An admin cannot suspend/deactivate themselves or remove their own Super Admin role.
5. Removing `permissions.assign` from one's own non-super role via the matrix is rejected (no self-lockout from the feature).
6. Suspended/deactivated/pending users have **zero** effective permissions; archived roles grant nothing.
7. Permission changes apply on the **next request** of affected users (effective permissions computed per request, never embedded in the cookie).
8. Critical permissions (`users.assign_role`, `permissions.assign`, `settings.manage`) additionally require **recent reauthentication (≤ 5 min)** for the mutation that uses them *(proposed hardening; confirm with PO)*.
9. Denied attempts are written to the audit log with the missing permission.
10. **Hierarchy (added in slice 4):** you cannot act on a user whose effective permissions exceed yours (e.g. a User Admin cannot suspend or re-role a Super Admin). Refusals are audited as `denied`.
11. **Role hierarchy (slice 5):** you may only edit/rename/archive/matrix-change a role whose current permissions you hold; role permission changes need `permissions.assign` + recent reauth; a matrix save cannot remove your own `permissions.assign`.
12. Admins can never read passwords, MFA secrets, recovery codes, raw tokens or session tokens.

## 4. Change process
Permission catalog lives in code (`server/domain/permissions.ts`) and is seeded by migration. Adding a permission = code change + migration + matrix doc update + test. Roles are data and editable at runtime by authorised users.
