#!/usr/bin/env bash
# Starts the .NET API against a throw-away database (annaba_digital_e2e) inside the dev Postgres container.
# Emails are "sent" to the console sender; the log is read by the tests to follow one-time links.
set -euo pipefail
cd "$(dirname "$0")/../.."
mkdir -p e2e/.tmp
docker exec annaba-digital-dev-db psql -U annaba_digital -d postgres -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS annaba_digital_e2e WITH (FORCE)" -c "CREATE DATABASE annaba_digital_e2e" >/dev/null
: > e2e/.tmp/api.log
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="http://localhost:${E2E_API_PORT:-5180}"
export Api__ProxyKey="${E2E_PROXY_KEY:-e2e-proxy-key-0123456789-abcdefghijklmnop}"
export Api__Origin="http://localhost:${E2E_WEB_PORT:-3100}"
export ConnectionStrings__Default="Host=localhost;Port=5432;Database=annaba_digital_e2e;Username=annaba_digital;Password=annaba_digital_dev"
export Email__LogBodies=true
exec dotnet run --project api/src/AnnabaDigital.Api --no-launch-profile --urls "http://localhost:${E2E_API_PORT:-5180}" > e2e/.tmp/api.log 2>&1
