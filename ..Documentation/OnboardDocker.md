# Onboarding — Docker

The intended way to run StageZero. Both compose files target stages in
`StageZero/Dockerfile`, so the stage names `debug` and `release` are a contract with them.

## Prerequisites

| Need | Version | Check |
|---|---|---|
| Docker | any recent | `docker info` |
| Docker Compose | v2 | `docker compose version` |

## Run

```bash
git clone https://github.com/CodeLifter-Platform/StageZero.git
cd StageZero
docker compose -f beta.docker-compose.yml up --build
```

Or build the image directly — note the build context is the **repo root**, not
`StageZero/`, because the web project references `../Lifted.BlazorAuth.Basic`:

```bash
docker build -f StageZero/Dockerfile --target release -t stagezero:local .
docker run -p 8080:8080 -e ASPNETCORE_URLS=http://+:8080 --env-file .env -v stagezero-data:/app-data stagezero:local
```

**What you should see:** the log reports `Platform: Linux, Data Directory: /app-data`, then
`Database path: /app-data/stagezero.db`, then `No users found. Please visit /setup to create
your admin account`, then one `Email:` line saying whether codes will be emailed or logged.
The IP monitor and change-handler services start. `/` answers 200.

Go to `/setup` first — with no users, that is the only useful page. It asks for the admin
email and a password, and that is the account. No verification code; StageZero never sends
email. The two optional boxes (CodeLifter newsletter, StageZero update news) post to
codelifter.net, which sends one confirmation email; unticked, nothing is sent.

"Forgot password" writes a 6-digit code to the container log as a banner headed
`STAGEZERO PASSWORD RESET CODE`, with the account and the code on their own lines. See
**Forgotten password** below.

For the real deployment — release image on `127.0.0.1:5100`, optional `cloudflared`
sidecar — use `prod.docker-compose.yml` via `./docker-run.sh up prod` (or
`.\docker-run.ps1 up prod`), then follow [CLOUDFLARE_TUNNEL_SETUP.md](CLOUDFLARE_TUNNEL_SETUP.md).
More compose and debugging detail: [DOCKER_SETUP.md](DOCKER_SETUP.md).

## Test

The test suite is not run inside the image; run it on the host with the .NET SDK
([OnboardWeb.md](OnboardWeb.md#test)).

## The two stages

- **`release`** — the published app. What `beta.docker-compose.yml` targets.
- **`debug`** — a hot-reload dev environment: SDK image, `dotnet watch run`, polling file
  watcher. What `debug.docker-compose.yml` targets. Edits on the mounted source take effect
  without a rebuild.

## Forgotten password

`/forgot-password` writes the code to the container log; read it with
`docker logs <container> 2>&1 | grep -A6 "PASSWORD RESET CODE"` and enter it on
`/reset-password`. If the log is gone, reset from inside the container:
`docker exec -it <container> dotnet StageZero.dll reset-password you@example.com` prints a
one-time password. As a last resort, stop the container and delete the admin row from the
database on the mounted volume (`sqlite3 <STAGEZERO_DATA_DIR>/stagezero.db "DELETE FROM
Users;"`); `/setup` returns on the next visit and every DNS and tunnel setting survives.
Full steps: [README.md](../README.md#configuration).

## Gotchas

- **`/app-data` must be a mounted volume.** `DataPathService` detects the container and puts
  the database, logs, *and* the Data Protection keys there. Without a volume you lose all
  three on restart — and losing the keys means every user is logged out and form posts break
  until a reload.
- **The container runs as a non-root user** (`$APP_UID`, 1654) since the release audit. A
  named volume created against this image is owned correctly; a bind mount, or a volume
  from an install that ran as root, needs its ownership fixed once or the database can't
  be written: `docker run --rm -v stagezero-data:/app-data alpine chown -R 1654 /app-data`
  (for a bind mount, `sudo chown -R 1654 <dir>` on the host).
- **The build context is the repo root.** Building from inside `StageZero/` fails on the
  first `COPY`, because the auth library lives one level up.
- **The polling file watcher is required in `debug`.** inotify does not fire for edits made
  on the host and seen through a bind mount, so without `DOTNET_USE_POLLING_FILE_WATCHER`
  hot reload silently does nothing.
- **HTTPS via the compose files** mounts a dev certificate from `~/.aspnet/https` and needs
  `CERT_PASSWORD` in `.env`. Plain HTTP on 8080 is simpler for local work.
- **`.env` only reaches the container if you pass it.** The compose files do so with
  `env_file`; a plain `docker run` needs `--env-file .env`. `.env` itself is excluded from
  the image by `.dockerignore`, on purpose. Nothing in it is required: the one thing it can
  change for the login flow is hiding the optional signup boxes
  (`CodeLifter__SubscriptionsUrl=`).
