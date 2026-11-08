#!/usr/bin/env bash
# Crea el primer administrador de plataforma de un ambiente nuevo (solo si no existe ninguno).
# Uso, desde backend/deploy:   bash ./scripts/bootstrap-admin.sh .env.production
# Pide nombre, correo y contraseña por la terminal: no quedan en archivos ni en el historial.
set -euo pipefail

ENV_FILE="${1:?Uso: ./scripts/bootstrap-admin.sh .env.X}"
cd "$(dirname "$0")/.."
export VERSION="${VERSION:?Fija VERSION a la versión desplegada (docker image ls bakerypos-api)}"

read -r -p "Nombre: " name
read -r -p "Correo: " email
read -r -s -p "Contraseña (mínimo 12): " password; echo

BOOTSTRAP_ADMIN_NAME="$name" BOOTSTRAP_ADMIN_EMAIL="$email" BOOTSTRAP_ADMIN_PASSWORD="$password" \
  docker compose --env-file "$ENV_FILE" -f docker-compose.yml run --rm --no-deps \
  -e BOOTSTRAP_ADMIN_NAME -e BOOTSTRAP_ADMIN_EMAIL -e BOOTSTRAP_ADMIN_PASSWORD \
  api bootstrap-admin
