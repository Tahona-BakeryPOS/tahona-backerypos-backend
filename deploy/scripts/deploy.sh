#!/usr/bin/env bash
# Deploys (or updates) an environment on the VPS: prior backup → images → migrations → API and proxy.
# Usage, from backend/deploy:   bash ./scripts/deploy.sh .env.production
# Requirements: Docker with Compose v2 and this repo up to date (git pull) at the version to deploy.
set -euo pipefail

ENV_FILE="${1:?Usage: ./scripts/deploy.sh .env.staging|.env.production}"
cd "$(dirname "$0")/.."
[ -f "$ENV_FILE" ] || { echo "$ENV_FILE does not exist (copy .env.deploy.example)"; exit 1; }

# Version = this repo's commit, unless VERSION is set to go back to an already built one.
if [ -z "${VERSION:-}" ]; then
  export VERSION="$(git -C .. rev-parse --short HEAD)"
fi
echo "Deploying version $VERSION with $ENV_FILE"

compose() { docker compose --env-file "$ENV_FILE" -f docker-compose.yml "$@"; }

# 1. Database up and a backup before migrating (if there was already data).
compose up -d db
compose exec -T db sh -c 'until pg_isready -U bakerypos -d bakerypos; do sleep 2; done'
bash ./scripts/backup.sh "$ENV_FILE"

# 2. Images (built on the VPS; the previous ones stay to roll back).
compose --profile migrate build migrate api

# 3. Migrations with the bundle (additive only; if they fail, the previous API keeps running).
compose --profile migrate run --rm migrate

# 4. API and proxy at the new version.
compose up -d api proxy
compose ps
echo "Done. Check https://$(grep -E '^API_DOMAIN=' "$ENV_FILE" | cut -d= -f2-)/health"
