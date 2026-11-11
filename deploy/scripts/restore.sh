#!/usr/bin/env bash
# Restores a backup from scripts/backup.sh. DESTRUCTIVE: it replaces the environment's database (runbook "Restoring a backup").
# Usage, from backend/deploy:
#   bash ./scripts/restore.sh .env.production /var/backups/bakerypos/bakerypos-production-20261028T033000Z.dump
# If the keys file with the same timestamp (…-dpkeys.tar.gz) exists, the keys are restored too.
set -euo pipefail

ENV_FILE="${1:?Usage: ./scripts/restore.sh .env.X file.dump}"
DUMP="${2:?The .dump file is missing}"
cd "$(dirname "$0")/.."
set -a; . "./$ENV_FILE"; set +a
: "${ENVIRONMENT:?}"
export VERSION="${VERSION:-restore}"
[ -s "$DUMP" ] || { echo "It does not exist or is empty: $DUMP"; exit 1; }

echo "The database of '$ENVIRONMENT' is going to be REPLACED with $DUMP."
read -r -p "Type the environment name to confirm: " answer
[ "$answer" = "$ENVIRONMENT" ] || { echo "Cancelled."; exit 1; }

compose() { docker compose --env-file "$ENV_FILE" -f docker-compose.yml "$@"; }

# Backup of the current state before replacing it.
bash ./scripts/backup.sh "$ENV_FILE"

compose stop api proxy
compose up -d db
compose exec -T db sh -c 'until pg_isready -U bakerypos -d bakerypos; do sleep 2; done'
compose exec -T db pg_restore -U bakerypos -d bakerypos --clean --if-exists --no-owner < "$DUMP"

keys="${DUMP%.dump}-dpkeys.tar.gz"
if [ -s "$keys" ]; then
  docker run --rm -i -v "bakerypos_${ENVIRONMENT}_dpkeys:/keys" alpine:3.22 sh -c 'rm -rf /keys/* && tar -C /keys -xzf -' < "$keys"
  echo "Llaves de Data Protection restauradas."
else
  echo "WARNING: there is no keys file for this backup; the Bold and Siigo credentials may not be readable."
fi

compose up -d api proxy
echo "Restore finished. Check /health and sign in to the backoffice."
