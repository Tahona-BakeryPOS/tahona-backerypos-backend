# Runbooks de operación (fase 10, D-10)

Un VPS por ambiente (staging y producción) con Docker Compose: PostgreSQL 17, API, migraciones (bundle) y Caddy
(proxy con HTTPS automático del API). Archivos en este repo, carpeta `deploy/`. Los clientes web se despliegan por separado. Todos los comandos se corren en el
VPS desde `deploy/` con un usuario que puede usar Docker.

> Nada de esto se ha ejecutado todavía: es la guía para el primer despliegue. Credenciales reales solo en el `.env`
> del VPS (permisos 600) y en tu gestor de secretos; nunca en el repo ni en el chat.

## 1. Preparar un VPS nuevo

1. Ubuntu 24.04 LTS, 2 vCPU y 4 GB de RAM alcanzan para varias panaderías. Abrir solo 22 (SSH con llave), 80 y 443:
   `ufw allow OpenSSH && ufw allow 80,443/tcp && ufw allow 443/udp && ufw enable`.
2. Instalar Docker Engine con el plugin de Compose (repositorio oficial de Docker) y git.
3. DNS: registro A del dominio del API (p. ej. `api-staging.tupanaderia.co` o `api.tupanaderia.co`, el `API_DOMAIN` del `.env`) hacia la IP del VPS.
4. Clonar este repo en `/opt/bakerypos/backend`.
5. `cp deploy/.env.deploy.example deploy/.env.production` (o `.env.staging`), `chmod 600`, y llenar los valores
   (generar contraseñas con `openssl rand -base64 128 | tr -dc 'A-Za-z0-9' | head -c 64`). Distintos por ambiente.

## 2. Primer despliegue

```bash
bash ./scripts/deploy.sh .env.production
```

Hace backup (vacío la primera vez), construye las imágenes, aplica las migraciones con el bundle y levanta la API y el proxy.
Caddy pide el certificado al primer acceso. Luego:

1. `curl https://API_DOMAIN/health` → `Healthy`.
2. Crear el administrador de plataforma (solo si no existe ninguno; pide los datos por la terminal):
   `VERSION=<versión que imprimió deploy.sh> bash ./scripts/bootstrap-admin.sh .env.production`.
3. Con ese administrador, `POST https://API_DOMAIN/api/v1/auth/login` y `POST /api/v1/plataforma/empresas` crean la primera empresa (la respuesta trae la contraseña de un solo uso del propietario).
4. Programar el backup diario (sección 4) y el monitoreo (sección 7).

## 3. Actualizar a una versión nueva

1. `git -C /opt/bakerypos/backend pull` (la rama/etiqueta aprobada).
2. Fuera de la hora pico: `bash ./scripts/deploy.sh .env.production`. El script hace backup antes de migrar; si la
   migración falla, la API anterior sigue corriendo.
3. Verificar `/health` y hacer una venta de prueba en una caja de la sede de pruebas.

**Volver a la versión anterior** (solo si la migración nueva no se aplicó o fue aditiva, como todas hasta hoy):
`VERSION=<versión anterior> docker compose --env-file .env.production -f docker-compose.yml up -d api proxy`
(las imágenes anteriores siguen en el VPS: `docker image ls bakerypos-api`). Si hace falta deshacer datos, sección 5.

## 4. Backups

- `bash ./scripts/backup.sh .env.production`: `pg_dump` (formato custom) **y** las llaves de Data Protection en
  `BACKUP_DIR`, con retención de `BACKUP_RETENTION_DAYS` días.
- Diario por cron (a las 3:30 del servidor):
  `30 3 * * * cd /opt/bakerypos/backend/deploy && bash ./scripts/backup.sh .env.production >> /var/log/bakerypos-backup.log 2>&1`
- **Fuera del servidor** (obligatorio en producción): copiar `BACKUP_DIR` a otro proveedor cada día (p. ej. `rclone`
  hacia un bucket con versionado, o `rsync` a otro equipo). Un backup que solo vive en el VPS no sirve si se pierde el VPS.
- Probar una restauración en staging al menos una vez al mes (sección 5).
- Las llaves de Data Protection cifran las credenciales de Bold y Siigo: sin ellas hay que volver a escribirlas en
  Configuración → Integraciones y Facturación electrónica. Guárdalas con el mismo cuidado que los backups.

## 5. Restaurar un backup

```bash
bash ./scripts/restore.sh .env.production /var/backups/bakerypos/bakerypos-production-AAAAMMDDTHHMMSSZ.dump
```

Pide escribir el nombre del ambiente, hace un backup del estado actual, detiene la API y el proxy, restaura la base (y las
llaves si está su archivo) y vuelve a levantar todo. Después: `/health`, una consulta de ventas, y avisar a las sedes que las ventas
posteriores al backup se perdieron (revisar contra los comprobantes y los documentos en Siigo).

## 6. Recuperación ante incidentes

| Síntoma | Qué revisar | Acción |
|---|---|---|
| `/health` no responde | `docker compose ... ps`, `docker compose ... logs --tail=200 api proxy` | `docker compose ... up -d`; si el VPS no arranca, nuevo VPS + sección 1 + restaurar el último backup |
| `/health` responde `Unhealthy` | `logs db`, espacio en disco (`df -h`) | Liberar disco (backups viejos, `docker image prune`) y `up -d db` |
| La API no arranca tras desplegar | `logs api`: falta `Jwt__Secret` (≥ 32), `DataProtection__KeysPath` o la cadena | Corregir el `.env` y `up -d api`, o volver a la versión anterior (sección 3) |
| Certificado HTTPS vencido o no emitido | `logs proxy` (Let's Encrypt) | DNS correcto y puertos 80/443 abiertos; `docker compose ... restart proxy` |
| Cobros de Bold quedan "pendientes" | Pagos (consulta de pagos del API); webhook `https://API_DOMAIN/api/v1/webhooks/pagos/bold` configurado en Bold | Resolver a mano con el número de aprobación (I-23) |
| Facturas en "sin confirmar" o "reintentando" | Facturas electrónicas → intentos; estado de Siigo | Corregir la causa (credenciales, emparejamientos) y reintentar; nunca marcar aceptada a mano |
| Credenciales de Bold/Siigo ilegibles | Se perdió el volumen de llaves | Restaurar las llaves del backup o volver a escribir las credenciales |
| Una caja no puede cerrar | Pagos pendientes de esa sesión (D-12) | Resolver los pagos pendientes y cerrar |

Rotación de secretos: `JWT_SECRET` (cierra todas las sesiones: los cajeros vuelven a entrar con PIN);
`POSTGRES_PASSWORD` solo cambia con `ALTER USER bakerypos PASSWORD '...'` dentro de `db` y luego en el `.env`.

## 7. Monitoreo mínimo

- Un monitor externo de disponibilidad (p. ej. UptimeRobot o Better Stack, gratis) sobre `https://API_DOMAIN/health`
  cada 5 minutos, con alerta al correo/WhatsApp del dueño.
- Logs: `docker compose --env-file .env.production -f docker-compose.yml logs -f api` (Serilog a consola). Para
  conservarlos, configurar la rotación de logs de Docker (`/etc/docker/daemon.json`, `max-size` y `max-file`).
- Revisar cada semana: espacio en disco, `/var/log/bakerypos-backup.log` y que haya copias fuera del servidor.

## 8. Pruebas antes de producción

1. **Recorrido de API** en staging (OpenAPI en `/openapi/v1.json`): login, vincular caja, abrir caja, venta en efectivo, anulación, devolución, cierre e informe.
2. **Bold** (criterio de la fase 6): cuenta en sandbox, webhook apuntando a staging, cobro de prueba desde
   Configuración → Integraciones y una venta con datáfono; confirmar que llega el webhook firmado.
3. **Siigo** (fases 7 y 9): empresa en habilitación, factura de prueba, una venta facturada, una anulación (nota
   crédito motivo 2) y una devolución (motivo 1) aceptadas con CUFE/CUDE. Verificar `taxed_price` y descuentos en las notas.
4. **Carga**: `k6 run ... backend/tests/load/sales-load.js` contra staging (variables en el encabezado del script);
   umbrales: p95 < 800 ms y menos de 1 % de errores con 20 cajas simultáneas.
5. **Restauración**: restaurar en staging el backup de la noche anterior.
