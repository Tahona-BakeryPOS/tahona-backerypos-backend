# Operations runbooks (phase 10, D-10)

One VPS per environment (staging and production) with Docker Compose: PostgreSQL 17, API, migrations (bundle) and Caddy
(automatic-HTTPS proxy for the API). Files are in this repo, in the `deploy/` folder. Web clients are deployed separately. All commands are run on the
VPS from `deploy/` with a user that can use Docker.

> None of this has been run yet: it is the guide for the first deployment. Real credentials only in the VPS `.env`
> (permissions 600) and in your secrets manager; never in the repo or in chat.

## 1. Preparing a new VPS

1. Ubuntu 24.04 LTS, 2 vCPU and 4 GB of RAM are enough for several bakeries. Open only 22 (SSH with a key), 80 and 443:
   `ufw allow OpenSSH && ufw allow 80,443/tcp && ufw allow 443/udp && ufw enable`.
2. Install Docker Engine with the Compose plugin (Docker's official repository) and git.
3. DNS: an A record for the API domain (e.g. `api-staging.tubakery.co` or `api.tubakery.co`, the `API_DOMAIN` in the `.env`) pointing to the VPS IP.
4. Clone this repo into `/opt/bakerypos/backend`.
5. `cp deploy/.env.deploy.example deploy/.env.production` (or `.env.staging`), `chmod 600`, and fill in the values
   (generate passwords with `openssl rand -base64 128 | tr -dc 'A-Za-z0-9' | head -c 64`). Different per environment.

## 2. First deployment

```bash
bash ./scripts/deploy.sh .env.production
```

It takes a backup (empty the first time), builds the images, applies the migrations with the bundle and starts the API and the proxy.
Caddy requests the certificate on first access. Then:

1. `curl https://API_DOMAIN/health` → `Healthy`.
2. Create the platform administrator (only if none exists; it asks for the data in the terminal):
   `VERSION=<version printed by deploy.sh> bash ./scripts/bootstrap-admin.sh .env.production`.
3. With that administrator, `POST https://API_DOMAIN/api/v1/auth/login` and `POST /api/v1/plataforma/empresas` create the first company (the response carries the owner's one-time password).
4. Schedule the daily backup (section 4) and monitoring (section 7).

## 3. Updating to a new version

1. `git -C /opt/bakerypos/backend pull` (the approved branch/tag).
2. Outside peak hours: `bash ./scripts/deploy.sh .env.production`. The script takes a backup before migrating; if the
   migration fails, the previous API keeps running.
3. Check `/health` and make a test sale on a register of the test branch.

**Rolling back to the previous version** (only if the new migration was not applied or was additive, like all of them so far):
`VERSION=<previous version> docker compose --env-file .env.production -f docker-compose.yml up -d api proxy`
(the previous images stay on the VPS: `docker image ls bakerypos-api`). If data must be undone, section 5.

## 4. Backups

- `bash ./scripts/backup.sh .env.production`: `pg_dump` (custom format) **and** the Data Protection keys into
  `BACKUP_DIR`, with a retention of `BACKUP_RETENTION_DAYS` days.
- Daily by cron (at 3:30 server time):
  `30 3 * * * cd /opt/bakerypos/backend/deploy && bash ./scripts/backup.sh .env.production >> /var/log/bakerypos-backup.log 2>&1`
- **Off the server** (mandatory in production): copy `BACKUP_DIR` to another provider every day (e.g. `rclone`
  to a versioned bucket, or `rsync` to another machine). A backup that only lives on the VPS is useless if the VPS is lost.
- Test a restore on staging at least once a month (section 5).
- The Data Protection keys encrypt the Bold and Siigo credentials: without them the credentials must be typed again in
  Configuración → Integraciones and Facturación electrónica. Keep them with the same care as the backups.

## 5. Restoring a backup

```bash
bash ./scripts/restore.sh .env.production /var/backups/bakerypos/bakerypos-production-YYYYMMDDTHHMMSSZ.dump
```

It asks you to type the environment name, takes a backup of the current state, stops the API and the proxy, restores the database (and the
keys if their file is present) and brings everything back up. Afterwards: `/health`, a sales query, and tell the branches that sales
made after the backup were lost (check against the receipts and the documents in Siigo).

## 6. Incident recovery

| Symptom | What to check | Action |
|---|---|---|
| `/health` does not respond | `docker compose ... ps`, `docker compose ... logs --tail=200 api proxy` | `docker compose ... up -d`; if the VPS does not boot, a new VPS + section 1 + restore the latest backup |
| `/health` responds `Unhealthy` | `logs db`, disk space (`df -h`) | Free disk (old backups, `docker image prune`) and `up -d db` |
| The API does not start after deploying | `logs api`: missing `Jwt__Secret` (≥ 32), `DataProtection__KeysPath` or the connection string | Fix the `.env` and `up -d api`, or roll back (section 3) |
| HTTPS certificate expired or not issued | `logs proxy` (Let's Encrypt) | Correct DNS and ports 80/443 open; `docker compose ... restart proxy` |
| Bold payments stay "pending" | Payments (the API's payment query); webhook `https://API_DOMAIN/api/v1/webhooks/pagos/bold` configured in Bold | Resolve by hand with the approval number (I-23) |
| Invoices "unconfirmed" or "retrying" | Electronic invoices → attempts; Siigo status | Fix the cause (credentials, pairings) and retry; never mark as accepted by hand |
| Bold/Siigo credentials unreadable | The keys volume was lost | Restore the keys from the backup or type the credentials again |
| A register cannot close | Pending payments of that session (D-12) | Resolve the pending payments and close |

Secret rotation: `JWT_SECRET` (closes all sessions: cashiers sign in again with their PIN);
`POSTGRES_PASSWORD` only changes with `ALTER USER bakerypos PASSWORD '...'` inside `db` and then in the `.env`.

## 7. Minimum monitoring

- An external availability monitor (e.g. UptimeRobot or Better Stack, free) on `https://API_DOMAIN/health`
  every 5 minutes, with an alert to the owner's email/WhatsApp.
- Logs: `docker compose --env-file .env.production -f docker-compose.yml logs -f api` (Serilog to console). To
  keep them, configure Docker log rotation (`/etc/docker/daemon.json`, `max-size` and `max-file`).
- Review weekly: disk space, `/var/log/bakerypos-backup.log` and that off-server copies exist.

## 8. Tests before production

1. **API walkthrough** on staging (OpenAPI at `/openapi/v1.json`): sign in, pair a register, open the cash session, cash sale, void, return, close and report.
2. **Bold** (phase 6 criterion): a sandbox account, the webhook pointing to staging, a test charge from
   Configuración → Integraciones and a card-terminal sale; confirm the signed webhook arrives.
3. **Siigo** (phases 7 and 9): a company in the test environment, a test invoice, an invoiced sale, a void (credit
   note reason 2) and a return (reason 1) accepted with CUFE/CUDE. Check `taxed_price` and discounts in the notes.
4. **Load**: `k6 run ... tests/load/sales-load.js` against staging (variables in the script header);
   thresholds: p95 < 800 ms and less than 1 % errors with 20 simultaneous registers.
5. **Restore**: restore last night's backup on staging.
