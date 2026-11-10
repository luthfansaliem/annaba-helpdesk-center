#!/usr/bin/env bash
# Restore drill: proves that a database dump and its files archive (made together by the compose `backup` service) bring the helpdesk back.
# It restores the dump into a THROW-AWAY PostgreSQL container (never touches a real database), unpacks the files archive into a temporary folder,
# and checks that every attachment the database says exists has its bytes. Exit status 0 = the backup is usable, 1 = something is wrong.
#
#   deploy/restore-drill.sh annaba_digital-20260101-020000.dump [annaba_digital-20260101-020000.files.tgz]
#
#   PG_IMAGE   image of the throw-away server (default postgres:16-alpine; use your production major version)
# Needs docker. Without the files archive only the database part is checked (and the script says so).
set -euo pipefail
export MSYS_NO_PATHCONV=1   # Git Bash on Windows would otherwise rewrite container paths

DUMP="${1:?usage: restore-drill.sh <dump> [files.tgz]}"; FILES="${2:-}"
IMAGE="${PG_IMAGE:-postgres:16-alpine}"
[ -s "$DUMP" ] || { echo "No such dump: $DUMP" >&2; exit 2; }
[ "$(head -c 5 "$DUMP")" = "PGDMP" ] || { echo "$DUMP is not a pg_dump custom-format file." >&2; exit 2; }
if [ -n "$FILES" ]; then [ -s "$FILES" ] && gzip -t "$FILES" || { echo "$FILES is missing or not a valid .tgz." >&2; exit 2; }; fi

C="drill-db-$$"; TMP="$(mktemp -d)"
cleanup() { docker rm -f "$C" >/dev/null 2>&1 || true; rm -rf -- "${TMP:?}"; }
trap cleanup EXIT

echo "== Starting a throw-away $IMAGE"
docker run -d --name "$C" -e POSTGRES_PASSWORD=drill -e POSTGRES_DB=drill "$IMAGE" >/dev/null
for _ in $(seq 1 60); do docker exec "$C" pg_isready -U postgres -d drill >/dev/null 2>&1 && break; sleep 1; done
docker exec "$C" pg_isready -U postgres -d drill >/dev/null || { echo "The scratch database did not start." >&2; exit 2; }

echo "== Restoring $(basename "$DUMP")"
# docker.exe on Windows needs a Windows path for the host side (Git Bash paths such as /c/Users/... are not understood)
hostpath() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else echo "$1"; fi; }
docker cp "$(hostpath "$DUMP")" "$C:/tmp/restore.dump"
# --no-owner: the dump's owner role (annaba_digital) does not exist in the scratch server
if ! docker exec "$C" pg_restore -U postgres -d drill --no-owner --exit-on-error /tmp/restore.dump; then echo "RESTORE FAILED" >&2; exit 1; fi
q() { docker exec "$C" psql -U postgres -d drill -At -c "$1"; }

fail=0
migration="$(q 'SELECT migration_id FROM "__EFMigrationsHistory" ORDER BY migration_id DESC LIMIT 1' || true)"
echo "   latest migration : ${migration:-?}"
for t in tenants users tickets attachments; do echo "   $t: $(q "SELECT count(*) FROM $t" || echo '?')"; done
[ -n "$migration" ] || { echo "No migration history found: not an application database?" >&2; fail=1; }

live="$(q 'SELECT count(*) FROM attachments WHERE deleted_at IS NULL')"
if [ -z "$FILES" ]; then
  echo "== No files archive given: the database part restored; $live attachment(s) point at files that were NOT checked."
  [ "$fail" -eq 0 ] && echo "DRILL PASSED (database only)" || echo "DRILL FAILED"; exit "$fail"
fi

echo "== Unpacking $(basename "$FILES")"
tar xzf "$FILES" -C "$TMP"
q 'SELECT storage_key FROM attachments WHERE deleted_at IS NULL ORDER BY 1' > "$TMP.keys"
missing=0; checked=0
while IFS= read -r key; do
  [ -n "$key" ] || continue
  checked=$((checked + 1))
  [ -s "$TMP/$key" ] || { missing=$((missing + 1)); [ "$missing" -le 10 ] && echo "   MISSING: $key"; }
done < "$TMP.keys"
total_files="$(find "$TMP" -type f ! -name '*.tmp' | wc -l | tr -d ' ')"
rm -f -- "$TMP.keys"
echo "   attachments checked : $checked   missing bytes : $missing   files in archive : $total_files"
[ "$missing" -eq 0 ] || fail=1
[ "$fail" -eq 0 ] && echo "DRILL PASSED" || echo "DRILL FAILED"
exit "$fail"
