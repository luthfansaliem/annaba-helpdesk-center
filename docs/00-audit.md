# 00 — Repository Audit (baseline)

Repo: `luthfansaliem/annaba-application-template` · branch `main` @ `06a7230` · audited 2026-11-04.

## 1. Stack
Next.js 15.5 (App Router) · React 19 · TypeScript (strict) · lucide-react · Vitest + Testing Library · ESLint 9. No Tailwind, no UI kit. Styling is a single `src/app/globals.css` (263 lines) with CSS variables.

## 2. What exists
| Area | State |
|---|---|
| Routes | `/` → redirects to `/login`; `/login`, `/dashboard`, `/employees` (CRUD specimen), `/components` (gallery) |
| Components | `ui/` (Button, Card/Panel, Badge, Field/TextInput/SelectInput/Checkbox, Tabs, Toast, Avatar), `data/` (DataTable, Pagination, FilterBar, ConfirmDialog, EmptyState), `charts/` (StatCard, LineChart, DonutChart, Sparkline, lists), `auth/` (AuthInput, PasswordInput, SocialButton…), `AppShell`/`Sidebar`/`Topbar` |
| Nav | Config-driven (`src/config/nav.ts`), two variants |
| Data | Static dummy data in `src/data/dummy.ts` |
| Tests | 9 component tests (all pass) |

## 3. What does NOT exist (all server-side)
No database, ORM, migrations, API routes, Server Actions, middleware, session/cookie handling, password hashing, email adapter, rate limiting, audit/security logging, env/secret handling, or CI. **Every infrastructure choice is therefore new** and must be documented per the brief (see `04-decisions-and-assumptions.md`).

The current `/login` "Sign In" button simply navigates to `/dashboard` — there is **no authentication**. All dashboard/employee data is hard-coded.

## 4. Reusable as-is
Design tokens (`:root` variables), Button/Card/Badge/Tabs/Toast/Field primitives, DataTable + Pagination + FilterBar (will need server-driven sorting/paging mode), ConfirmDialog, StatCard/LineChart/DonutChart, Avatar, AppShell structure.

## 5. Defects & gaps found (relative to the brief)
| ID | Finding | Severity | Planned fix |
|---|---|---|---|
| D1 | **Mobile navigation missing**: sidebar is `display:none` ≤820px with no hamburger/drawer | High | Drawer with X, backdrop, select-to-close (NAV-M) |
| D2 | **"Collapse menu" is decorative** (non-interactive div) | High | Real expanded/compact desktop sidebar (NAV-D) |
| D3 | Active group does not auto-expand; all groups open by default | Med | Auto-expand active group |
| D4 | 13 nav items are dead `#` links; "More", "Export", detail "⋯" etc. are partly decorative | Med | Remove or implement; no placeholder controls |
| D5 | Pagination total "248 results" and rows-per-page are fake (client-only) | Med | Server-driven table mode |
| D6 | DataTable has no sort behaviour (icon only), no loading/error states | Med | Add sort, loading, error |
| D7 | No focus trap / Esc handling in ConfirmDialog modal; no focus restoration | Med | Shared accessible Dialog/Drawer |
| D8 | No dark-mode/`prefers-reduced-motion` handling | Low | Out of scope unless requested |
| D9 | Breakpoints are ad hoc (1300/1180/900/820); required verification widths are 1440/1366/1024/768/390/375 | Med | Rationalise breakpoints |
| D10 | `tsconfig.tsbuildinfo` present locally (ignored by git — OK) | — | none |
| D11 | Some SVG path literals contain redundant zeros (e.g. `30.300`) — harmless | Low | Clean up opportunistically |

## 6. Design-baseline discrepancy (needs PO confirmation)
The brief refers to an "approved 90% visual density" and "approved desktop typography". **The repository contains no such definition** — there is no zoom, no density token, and base font is 13px. We will **not** hard-code browser zoom. Proposed interpretation: define density as explicit tokens (base font, control height 34–38px, card padding, row height) calibrated to match the approved reference at 100% zoom. **Decision D-08** asks the PO to supply/confirm the reference (screenshots or measurements).

## 7. Reuse vs. rewrite policy
Keep the existing visual language and primitives. Extend (not rewrite) AppShell for collapse/mobile. Replace `employees`/`dashboard` dummy pages with real IAM pages in Phase 4; the `/employees` CRUD specimen and `/components` gallery may stay as design-system references.
