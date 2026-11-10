#!/usr/bin/env bash
# Copies the PostgreSQL dumps made by the compose `backup` service from the VPS to THIS machine (read-only on the VPS).
# Run it from your own computer: the point is a copy that survives losing the VPS.
#   VPS_HOST       ssh target                       (default dev@187.77.159.9)
#   BACKUP_DEST    where copies go                  (default ~/annaba-vps-backups, mode 700, outside the repo)
#   KEEP_LOCAL_DAYS delete local copies older than  (default 60; 0 keeps everything)
# Needs a working non-interactive ssh (ssh-add the key first). Safe to run repeatedly: only missing files are fetched.
set -euo pipefail

HOST="${VPS_HOST:-dev@187.77.159.9}"
DEST="${BACKUP_DEST:-$HOME/annaba-vps-backups}"
KEEP="${KEEP_LOCAL_DAYS:-60}"
SSH=(ssh -n -o BatchMode=yes -o ConnectTimeout=15 "$HOST")

mkdir -p "$DEST"; chmod 700 "$DEST"

# The container name changes with every Coolify redeploy, so look it up each time.
C="$("${SSH[@]}" "docker ps --format '{{.Names}}' | grep '^backup-' | head -n 1")"
[ -n "$C" ] || { echo "No running backup container on $HOST." >&2; exit 1; }

fetched=0; kept=0
while IFS= read -r f; do
  [[ "$f" =~ ^annaba_digital-[0-9]{8}-[0-9]{6}\.(dump|files\.tgz)$ ]] || continue   # only files that look like our dumps and their files archives
  if [ -s "$DEST/$f" ]; then kept=$((kept + 1)); continue; fi
  "${SSH[@]}" "docker exec $C cat /backups/$f" > "$DEST/$f.part"
  # A pg_dump custom-format file starts with "PGDMP"; anything else means a truncated or wrong transfer.
  if [[ "$f" == *.dump ]]; then
    [ "$(head -c 5 "$DEST/$f.part")" = "PGDMP" ] || { rm -f "$DEST/$f.part"; echo "Bad dump header in $f, skipped." >&2; continue; }
  else
    gzip -t "$DEST/$f.part" 2>/dev/null || { rm -f "$DEST/$f.part"; echo "Bad archive $f, skipped." >&2; continue; }   # a truncated transfer fails the gzip check
  fi
  mv "$DEST/$f.part" "$DEST/$f"; chmod 600 "$DEST/$f"; fetched=$((fetched + 1))
done < <("${SSH[@]}" "docker exec $C ls -1 /backups")

if [ "$KEEP" -gt 0 ]; then find "$DEST" \( -name 'annaba_digital-*.dump' -o -name 'annaba_digital-*.files.tgz' \) -mtime "+$KEEP" -delete; fi
echo "Fetched $fetched new, $kept already here -> $DEST ($(ls "$DEST" | grep -c '\.dump$') dumps)."
