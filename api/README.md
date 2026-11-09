# Annaba Digital API (ASP.NET Core, .NET 10)

Backend for the Identity & Access starter kit (see `docs/13-adr-dotnet-backend.md`). **Phase 4, slices 1–3** — see `docs/14-…`, `docs/15-…` and `docs/16-phase4-slice3-report.md` for exactly what exists.

## Layout
```
src/AnnabaDigital.Domain          entities, permission catalog, error codes (no dependencies)
src/AnnabaDigital.Application     use-cases (AuthService, SessionService, AuthorizationService, AuditWriter), ports
src/AnnabaDigital.Infrastructure  EF Core + Npgsql, migrations, Argon2id, tokens, Postgres rate limiter, seeder
src/AnnabaDigital.Api             HTTP: auth handler, middleware (proxy gate, CSRF, session state), endpoints
tests/AnnabaDigital.Tests         xUnit + WebApplicationFactory + Testcontainers (real PostgreSQL)
```

## Run locally
```bash
npm run db:up                       # PostgreSQL 16 in Docker (localhost:5432)
export Api__ProxyKey=dev-proxy-key-0123456789abcdef0123456789abcdef
npm run api:run                     # Development: auto-migrates + seeds reference data + 4 demo users on http://localhost:5080
# UI proxy (same origin, adds the proxy key):
API_URL=http://localhost:5080 API_PROXY_KEY=$Api__ProxyKey npm run dev
```
Demo users (Development only, password `Passw0rd!demo`): sarah.wijaya@ / daniel.tan@ / maya.putri@ / rizky.pratama@`annabadigital.io`.

## Tests
```bash
npm run api:test        # needs Docker running (Testcontainers starts postgres:16-alpine)
```

## Migrations
```bash
cd api && dotnet tool restore
dotnet tool run dotnet-ef migrations add <Name> -p src/AnnabaDigital.Infrastructure -s src/AnnabaDigital.Infrastructure -o Persistence/Migrations
```
Production: apply migrations as a release step (never at app boot). `Api:AutoMigrate`/`SeedDevUsers` are Development-only and `StartupChecks` refuses insecure Production config.

## Email in development
`Email:Provider=console` (default) logs recipient + subject only. Development appsettings set `Email:LogBodies=true`, which prints the full message **including one-time links** so you can click them. Registration is closed by default; to try it set the `registrationMode` system setting to `"open"`.

## Production configuration (required)
`ConnectionStrings__Default`, `Api__Origin` (https), `Api__ProxyKey` (≥32), `Security__CsrfKey` (≥32), `Security__PasswordPepper` (≥32), `Security__EncryptionKeys__1` (32-byte base64; the built-in dev key is rejected) and `Security__CurrentKeyVersion`, `Email__Provider=resend`, `Email__ResendApiKey`, `Email__LogBodies=false`. The app refuses to start without them.
