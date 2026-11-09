# Annaba Digital – Identity & Access

Identity and access management starter kit: a Next.js 15 web app (repo root) in front of a .NET 10 API (`api/`) with PostgreSQL.
Sign-in with MFA and SSO (OIDC), users, roles and permissions, invitations, access requests and reviews, audit log, per-tenant settings and security policy, support mode for the platform operator, account closure and data export, English and Bahasa Indonesia, light and dark theme.

<!-- template:start -->
## Using this repository as a template

1. On GitHub choose **Use this template → Create a new repository** (private), then clone your new repository.
2. From the repository root run the init script. It renames the product everywhere in one pass: display name, .NET projects and namespaces, database and container names, the seed e-mail domain, and the deploy hosts.

   ```bash
   node scripts/init-project.mjs --dry-run --name "Acme Portal" --domain acme.io   # preview, writes nothing
   node scripts/init-project.mjs                                                  # asks for name, domain, staging host, VPS and repo
   ```

   It needs a clean working tree, only edits files tracked by git, and removes itself and this section when it finishes. Review the result with `git diff --stat`.
3. **Replace the logo.** Overwrite these files, keeping the names (transparent PNGs; "light" is the version shown on the light theme, "dark" the one on the dark theme):
   `public/brand/logo-light.png`, `logo-dark.png` (mark + wordmark), `logo-full-*.png` (with tagline), `mark-light.png`, `mark-dark.png` (mark only) and `src/app/icon.png` (square, 512 px favicon). PWA and Apple icons are generated from the mark. Brand colours are the CSS variables at the top of `src/app/globals.css`.
4. Check it still builds and passes: `npm ci && npm run typecheck && npm test` and `npm run api:test` (Docker must be running).
5. Create your own secrets (`deploy/env.example` lists them). Never reuse another project's keys, pepper or database.
6. `docs/22-staging-runbook.md` is the template's own project history and ops notes. Keep what applies, delete the rest.
<!-- template:end -->

## Quick start

```bash
npm ci
npm run db:up                                   # PostgreSQL 16 in Docker (localhost:5432)
export Api__ProxyKey=dev-proxy-key-0123456789abcdef0123456789abcdef
npm run api:run                                 # API on http://localhost:5080 (migrates and seeds in Development)
API_URL=http://localhost:5080 API_PROXY_KEY=$Api__ProxyKey npm run dev   # UI on http://localhost:3000
```

Demo users (Development only) are listed in `api/README.md`.

## Checks

```bash
npm run typecheck && npm run lint && npm test      # UI: types, lint, vitest
npm run api:test                                    # API: needs Docker (Testcontainers starts PostgreSQL)
npm run build && npm run test:e2e                   # end-to-end (Playwright, needs the dev database container)
```

## Structure
- `src/app` – routes (App Router); `src/middleware.ts` – API proxy and the script CSP
- `src/components` – `ui` primitives, `data` (tables, dialogs), `charts`, `iam` (user/role/invitation screens), `auth`
- `src/i18n` – `t("English text")` and the Bahasa Indonesia dictionary (`id.ts`)
- `src/config/nav.ts` – sidebar driven by permissions
- `api/` – .NET solution: `Domain`, `Application`, `Infrastructure`, `Api`, `Tests` (see `api/README.md`)
- `deploy/` – Coolify/Docker Compose deployment, env example, backup pull script
- `docs/` – product, architecture, security and operations documents (start at `docs/01-prd.md`; ops in `docs/22-staging-runbook.md`)
- `e2e/` – Playwright specs
