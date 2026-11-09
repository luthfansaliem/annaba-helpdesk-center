# 01 — Product Requirements Document (PRD)

Product: **Enterprise Identity & Access Management (IAM) Starter Kit** on the Azure Executive design system.
Version: 0.1 (draft for PO review). Priority: **P0** = required for acceptance, **P1** = required for release, **P2** = extension point.

## 1. Goals
Provide a reusable, secure foundation for internal enterprise systems and SaaS apps: authentication, user/role/permission management, sessions, auditing, profile and system settings — all **enforced server-side**.

## 2. Personas
| Persona | Needs |
|---|---|
| **Super Admin** | Full control incl. system settings, role/permission design; cannot be locked out (last-admin protection) |
| **User Admin** | Manage users & invitations; no permission-catalog or system-settings access |
| **Security Auditor** | Read-only audit logs, security events, sessions |
| **Standard User** | Own profile, own sessions, own activity, MFA |
| **Invitee / Registrant** | Accept invitation / register, verify email |

## 3. Scope
In: everything below. Out (explicit): multi-tenant data model (extension points only), SSO/OIDC/SAML providers (extension point: `external_identities` table), SMS MFA, WebAuthn (P2), billing.

## 4. Functional requirements

### 4.1 Authentication
| ID | Requirement | Pri |
|---|---|---|
| AUTH-001 | Login with email + password, show/hide, Remember Me (longer session), validation, generic error text, per-IP+per-account rate limiting, optional MFA step, safe same-origin `returnTo` | P0 |
| AUTH-002 | Registration (name, email, password, confirm, strength meter, terms), duplicate handling without enumeration, **disabled when mode = invitation-only** | P0 |
| AUTH-003 | Email verification: single-use, hashed, expiring token; resend (rate-limited); expired-token recovery; activates account | P0 |
| AUTH-004 | Forgot password: generic response always; hashed expiring single-use token; rate limit; email delivery | P0 |
| AUTH-005 | Reset password: token validation, policy check, single-use consumption, revoke existing sessions per policy, return to login | P0 |
| AUTH-006 | Lock screen: **server-side** session `locked` state; every request rejected until reauthentication; unlock via password (+MFA if enabled) | P0 |
| AUTH-007 | TOTP MFA: enroll (QR + manual key), verify, recovery codes (hashed, single-use), regenerate, disable with reauth; secrets encrypted at rest; audit events | P0 |
| AUTH-008 | Screens + recovery actions for: pending verification, active, suspended, deactivated, temporarily locked, session expired, access denied, invitation expired | P0 |

### 4.2 Dashboard
| ID | Requirement | Pri |
|---|---|---|
| DSH-001 | Admin dashboard KPIs: Total/Active/Suspended users, Pending invitations, Total roles, Active sessions — from DB, permission-gated | P0 |
| DSH-002 | Charts: user growth, status distribution, login activity, role distribution | P0 |
| DSH-003 | Panels: recent registrations, logins, role changes, security events, pending invitations, quick actions — each gated by its read permission | P0 |
| DSH-004 | Standard-user dashboard: status, roles, effective-permission summary, own activity, own sessions, security status, profile completion, quick links | P0 |
| DSH-005 | Loading / empty / error / populated states for every widget | P0 |

### 4.3 Users
| ID | Requirement | Pri |
|---|---|---|
| USR-001 | Users Overview (totals, pending verification, recent registrations, growth, distribution) | P0 |
| USR-002 | All Users: server-side search, filter (status, role, verified), sort, pagination, row actions, bulk actions (permission-gated), **`+ Add User` primary button top-right of header** | P0 |
| USR-003 | Add User (modal/drawer): name, email, roles, status *or* invitation mode; optional phone, department, title, avatar; duplicate detection; never emails passwords; audited | P0 |
| USR-004 | User details with tabs: Overview, Profile, Roles & Permissions, Sessions, Activity History, Security | P0 |
| USR-005 | Admin actions: edit, activate, suspend, deactivate, unlock, resend verification, revoke sessions, assign/remove roles; confirmation for sensitive ones; **last-super-admin protection**; no self-lockout | P0 |
| NAV-U1 | "Add User" must **not** appear in the sidebar | P0 |

### 4.4 Invitations
| ID | Requirement | Pri |
|---|---|---|
| INV-001 | Create invitation (email, intended roles, optional message), token hashed/single-use/expiring | P0 |
| INV-002 | List by state: pending / accepted / expired / revoked; revoke; resend (rotates token) | P0 |
| INV-003 | Acceptance page: set name+password (+ policy), activate account, assign intended roles atomically; expired/revoked/used handling | P0 |

### 4.5 Roles & Permissions
| ID | Requirement | Pri |
|---|---|---|
| ROLE-001 | Role list/create/edit/duplicate/archive, assign permissions, view assigned users; protected system roles (non-deletable, `super_admin` permission set immutable) | P0 |
| PERM-001 | Seeded permission catalog `resource.action` (users.read/create/update/suspend/assign_role, roles.*, permissions.read/assign, sessions.revoke, audit.read, security.read, settings.manage, invitations.*, …) | P0 |
| PERM-002 | Access Matrix: roles × permissions grouped by module, effective permissions, unsaved-change tracking, confirm-before-save, transactional save | P0 |
| PERM-003 | Server enforcement on **every** route/action; deny by default; no privilege escalation (cannot grant permissions you don't hold; cannot edit own role to gain access); permission changes take effect on active sessions on next request; audited | P0 |

### 4.6 Sessions, audit, security
| ID | Requirement | Pri |
|---|---|---|
| SES-001 | View current/active sessions (device, browser, IP-derived location label, created, last activity, expiry); revoke one / all others / sign out everywhere; raw tokens never exposed | P0 |
| SES-002 | Cookie config: `HttpOnly; Secure; SameSite=Lax; __Host-` prefix; idle + absolute timeout; token rotation on privilege change/login | P0 |
| AUD-001 | Audit log: actor, action, target, timestamp, outcome, metadata, safe before/after diff; filter + paginate + detail view; secrets redacted | P0 |
| SEC-001 | Security events: failed logins, lockouts, password resets, MFA enroll/remove, session revocation, suspicious activity, privilege changes; admin review (acknowledge) | P0 |
| SEC-002 | Retention policy (configurable) with purge job | P1 |

### 4.7 Profile & settings
| ID | Requirement | Pri |
|---|---|---|
| PRF-001 | View/edit profile, avatar upload (type/size validated), notification prefs | P0 |
| PRF-002 | Change email (verification to new address, reauth), change password (reauth, revoke others), MFA management, own sessions & activity | P0 |
| SET-001 | Personal settings: language, timezone, notifications, security prefs | P1 |
| SET-002 | System settings (`settings.manage`): registration mode, password policy, session lifetimes, lockout policy, MFA policy (off/optional/required), invitation expiry, audit retention, email config; secrets write-only & masked | P0 |

### 4.8 Navigation & UX
| ID | Requirement | Pri |
|---|---|---|
| NAV-D | Desktop: groups open/close manually, active group auto-expands, Collapse Menu toggles expanded ↔ compact (icons + tooltips), state persisted | P0 |
| NAV-M | Mobile: drawer opens from topbar button; closes via X, backdrop, menu selection, Esc; focus managed; no duplicate "Hide Menu" | P0 |
| UX-001 | Every interactive control functional; no decorative buttons | P0 |
| UX-002 | States per feature: loading, empty, populated, validation error, server error, permission denied, success, confirmation, expired session | P0 |
| UX-003 | Verified widths: 1440, 1366, 1024, 768, 390, 375; WCAG 2.1 AA contrast, keyboard operable, visible focus | P0 |
| UX-004 | Density achieved via tokens at 100 % zoom — **no CSS zoom/`transform: scale`** | P0 |

## 5. Non-functional requirements
| ID | Requirement |
|---|---|
| NFR-SEC-1 | OWASP ASVS L2 targets for V2 (Auth), V3 (Session), V4 (Access Control), V5 (Validation); OWASP Top 10 review at release gate |
| NFR-SEC-2 | Passwords: Argon2id; tokens: ≥256-bit CSPRNG, stored as SHA-256 hash; MFA secrets AES-256-GCM encrypted, key from env/KMS; recovery codes hashed |
| NFR-SEC-3 | CSRF: SameSite cookies + origin check + per-session token on mutating requests; security headers (CSP, HSTS, frame-ancestors) |
| NFR-SEC-4 | No credentials/tokens/secrets in logs, audit metadata, error messages, or client bundles; redaction utility with tests |
| NFR-SEC-5 | Uniform timing/response for login, forgot-password, registration to prevent enumeration |
| NFR-PERF | p95 < 300 ms for list endpoints at 10k users (indexed, server-paginated) |
| NFR-MNT | Layered modules (ui / application / domain / persistence / auth / authz / audit / infra adapters); adapters for email, rate limiter, storage |
| NFR-TEST | Unit + integration + e2e (browser) + a11y + negative/authorization tests; targeted runs per slice, full regression at release gate only |
| NFR-EXT | Tenant extension point documented (no tenant column implemented without decision D-06) |

## 6. Seed roles (proposed)
`super_admin` (all, protected), `user_admin`, `security_auditor`, `user` (self-service only). All editable except protected invariants.

## 7. Acceptance mapping (brief §18 → requirements)
1 AUTH-001 · 2 AUTH-002/003 · 3 AUTH-004/005 · 4 AUTH-006 · 5 AUTH-007 · 6 USR-* · 7 NAV-U1/USR-002 · 8 ROLE/PERM-* · 9 INV-* · 10 SES-* · 11 AUD/SEC-* · 12 DSH-* · 13 PRF/SET-* · 14 NAV-D/NAV-M · 15 UX-001 · 16 test plan · 17 threat-model review.
Each item is **Not started**; none may be marked complete without verified evidence.
