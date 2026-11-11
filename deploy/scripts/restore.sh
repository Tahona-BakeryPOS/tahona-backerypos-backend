#!/usr/bin/env bash
# Restaura un backup de scripts/backup.sh. DESTRUCTIVO: reemplaza la base del ambiente (runbook "Restaurar un backup").
# Uso, desde backend/deploy:
#   bash ./scripts/restore.sh .env.production /var/backups/bakerypos/bakerypos-production-20261028T033000Z.dump
# Si existe el archivo de llaves con el mismo sello (…-dpkeys.tar.gz) también se restauran las llaves.
set -euo pipefail

ENV_FILE="${1:?Uso: ./scripts/restore.sh .env.X archivo.dump}"
DUMP="${2:?Falta el archivo .dump}"
cd "$(dirname "$0")/.."
set -a; . "./$ENV_FILE"; set +a
: "${ENVIRONMENT:?}"
export VERSION="${VERSION:-restore}"
[ -s "$DUMP" ] || { echo "No existe o está vacío: $DUMP"; exit 1; }

echo "Se va a REEMPLAZAR la base de '$ENVIRONMENT' con $DUMP."
read -r -p "Escribe el nombre del ambiente para confirmar: " answer
[ "$answer" = "$ENVIRONMENT" ] || { echo "Cancelado."; exit 1; }

compose() { docker compose --env-file "$ENV_FILE" -f docker-compose.yml "$@"; }

# Copia de seguridad del estado actual antes de reemplazarlo.
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
  echo "AVISO: no hay archivo de llaves para este backup; las credenciales de Bold y Siigo podrían no leerse."
fi

compose up -d api proxy
echo "Restauración terminada. Revisa /health y entra al backoffice."
