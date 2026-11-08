#!/usr/bin/env bash
# Despliega (o actualiza) un ambiente en el VPS: backup previo → imágenes → migraciones → API y web.
# Uso, desde backend/deploy:   bash ./scripts/deploy.sh .env.production
# Requisitos: Docker con Compose v2; los repos backend y frontend ya actualizados (git pull) en la versión a desplegar.
set -euo pipefail

ENV_FILE="${1:?Uso: ./scripts/deploy.sh .env.staging|.env.production}"
cd "$(dirname "$0")/.."
[ -f "$ENV_FILE" ] || { echo "No existe $ENV_FILE (copia .env.deploy.example)"; exit 1; }

# Versión = commit del backend (y del frontend), salvo que se fije VERSION para volver a una ya construida.
if [ -z "${VERSION:-}" ]; then
  backend_rev=$(git -C .. rev-parse --short HEAD)
  frontend_dir=$(grep -E '^FRONTEND_DIR=' "$ENV_FILE" | cut -d= -f2-)
  frontend_rev=$(git -C "${frontend_dir:-../../frontend}" rev-parse --short HEAD)
  export VERSION="${backend_rev}-${frontend_rev}"
fi
echo "Desplegando versión $VERSION con $ENV_FILE"

compose() { docker compose --env-file "$ENV_FILE" -f docker-compose.yml "$@"; }

# 1. Base de datos arriba y backup antes de migrar (si ya había datos).
compose up -d db
compose exec -T db sh -c 'until pg_isready -U bakerypos -d bakerypos; do sleep 2; done'
bash ./scripts/backup.sh "$ENV_FILE"

# 2. Imágenes (se construyen en el VPS; las anteriores quedan para volver atrás).
compose --profile migrate build migrate api web

# 3. Migraciones con el bundle (solo aditivas; si fallan, la API anterior sigue corriendo).
compose --profile migrate run --rm migrate

# 4. API y frontend en la versión nueva.
compose up -d api web
compose ps
echo "Listo. Revisa https://$(grep -E '^DOMAIN=' "$ENV_FILE" | cut -d= -f2-)/health"
