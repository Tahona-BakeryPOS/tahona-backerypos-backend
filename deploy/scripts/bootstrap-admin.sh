#!/usr/bin/env bash
# Creates the first platform administrator of a new environment (only if none exists).
# Usage, from backend/deploy:   bash ./scripts/bootstrap-admin.sh .env.production
# It asks for name, email and password in the terminal: they are not left in files or in the history.
set -euo pipefail

ENV_FILE="${1:?Usage: ./scripts/bootstrap-admin.sh .env.X}"
cd "$(dirname "$0")/.."
export VERSION="${VERSION:?Set VERSION to the deployed version (docker image ls bakerypos-api)}"

read -r -p "Nombre: " name
read -r -p "Correo: " email
read -r -s -p "Password (minimum 12): " password; echo

BOOTSTRAP_ADMIN_NAME="$name" BOOTSTRAP_ADMIN_EMAIL="$email" BOOTSTRAP_ADMIN_PASSWORD="$password" \
  docker compose --env-file "$ENV_FILE" -f docker-compose.yml run --rm --no-deps \
  -e BOOTSTRAP_ADMIN_NAME -e BOOTSTRAP_ADMIN_EMAIL -e BOOTSTRAP_ADMIN_PASSWORD \
  api bootstrap-admin
