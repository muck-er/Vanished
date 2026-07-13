#!/usr/bin/env sh
set -eu

: "${POSTGRES_DB:=vanished}"
: "${POSTGRES_USER:=vanished}"
: "${BACKUP_DIR:=/backups}"

mkdir -p "$BACKUP_DIR"

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
target="${BACKUP_DIR}/vanished_${timestamp}.dump"

echo "[backup] creating ${target}"
pg_dump -Fc -U "$POSTGRES_USER" "$POSTGRES_DB" > "$target"

sha256sum "$target" > "${target}.sha256"
find "$BACKUP_DIR" -name 'vanished_*.dump' -mtime +14 -delete
find "$BACKUP_DIR" -name 'vanished_*.dump.sha256' -mtime +14 -delete

echo "[backup] done"
