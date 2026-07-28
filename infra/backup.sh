#!/bin/sh
# Continuum DB backup sidecar. Runs pg_dump on a loop, gzips to /backups, prunes old dumps.
# Uses the same postgres major as the db service so pg_dump matches the server.
set -eu

INTERVAL_HOURS="${BACKUP_INTERVAL_HOURS:-24}"
KEEP_DAYS="${BACKUP_KEEP_DAYS:-14}"
DB_HOST="${DB_HOST:-db}"
DB_USER="${DB_USER:-continuum}"
DB_NAME="${DB_NAME:-continuum}"

mkdir -p /backups
echo "[backup] started — every ${INTERVAL_HOURS}h, keep ${KEEP_DAYS}d, -> /backups"

while true; do
  TS=$(date -u +%Y%m%d-%H%M%S)
  OUT="/backups/continuum-${TS}.sql.gz"
  if pg_dump -h "$DB_HOST" -U "$DB_USER" -d "$DB_NAME" | gzip -c > "${OUT}.tmp"; then
    mv "${OUT}.tmp" "$OUT"
    echo "[backup] wrote ${OUT} ($(du -h "$OUT" | cut -f1))"
  else
    echo "[backup] FAILED at ${TS}" >&2
    rm -f "${OUT}.tmp"
  fi
  # Retention: remove dumps older than KEEP_DAYS.
  find /backups -name 'continuum-*.sql.gz' -type f -mtime +"${KEEP_DAYS}" -delete 2>/dev/null || true
  sleep "$((INTERVAL_HOURS * 3600))"
done
