#!/usr/bin/env bash
# Backup of an environment: database (pg_dump, custom format) and Data Protection keys (without them the stored
# Bold and Siigo credentials cannot be read). Deletes local backups older than BACKUP_RETENTION_DAYS.
# Usage, from backend/deploy:   bash ./scripts/backup.sh .env.production
# Daily cron (crontab -e of the user that runs Docker):
#   30 3 * * * cd /opt/bakerypos/backend/deploy && bash ./scripts/backup.sh .env.production >> /var/log/bakerypos-backup.log 2>&1
# The files stay on the VPS: also copy them off it (runbook, "Off-server backups").
set -euo pipefail

ENV_FILE="${1:?Usage: ./scripts/backup.sh .env.staging|.env.production}"
cd "$(dirname "$0")/.."
set -a; . "./$ENV_FILE"; set +a
: "${ENVIRONMENT:?}" "${BACKUP_DIR:?}"
export VERSION="${VERSION:-backup}"

stamp=$(date -u +%Y%m%dT%H%M%SZ)
mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"

compose() { docker compose --env-file "$ENV_FILE" -f docker-compose.yml "$@"; }

db_file="$BACKUP_DIR/bakerypos-$ENVIRONMENT-$stamp.dump"
compose exec -T db pg_dump -U bakerypos -d bakerypos -Fc > "$db_file"
[ -s "$db_file" ] || { echo "The database backup is empty"; rm -f "$db_file"; exit 1; }

keys_file="$BACKUP_DIR/bakerypos-$ENVIRONMENT-$stamp-dpkeys.tar.gz"
docker run --rm -v "bakerypos_${ENVIRONMENT}_dpkeys:/keys:ro" alpine:3.22 tar -C /keys -czf - . > "$keys_file"

chmod 600 "$db_file" "$keys_file"
find "$BACKUP_DIR" -name "bakerypos-$ENVIRONMENT-*" -type f -mtime +"${BACKUP_RETENTION_DAYS:-14}" -delete
echo "$(date -u +%FT%TZ) backup ok: $db_file ($(du -h "$db_file" | cut -f1)), $keys_file"
