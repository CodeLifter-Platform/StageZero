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
docker run -p 8080:8080 -e ASPNETCORE_URLS=http://+:8080 -v stagezero-data:/app-data stagezero:local
```

**What you should see:** the log reports `Platform: Linux, Data Directory: /app-data`, then
`Database path: /app-data/stagezero.db`, then `No users found. Please visit /setup to create
your admin account`. The IP monitor and change-handler services start. `/` answers 200.

Go to `/setup` first — with no users, that is the only useful page.

## The two stages

- **`release`** — the published app. What `beta.docker-compose.yml` targets.
- **`debug`** — a hot-reload dev environment: SDK image, `dotnet watch run`, polling file
  watcher. What `debug.docker-compose.yml` targets. Edits on the mounted source take effect
  without a rebuild.

## Gotchas

- **`/app-data` must be a mounted volume.** `DataPathService` detects the container and puts
  the database, logs, *and* the Data Protection keys there. Without a volume you lose all
  three on restart — and losing the keys means every user is logged out and form posts break
  until a reload.
- **The build context is the repo root.** Building from inside `StageZero/` fails on the
  first `COPY`, because the auth library lives one level up.
- **The polling file watcher is required in `debug`.** inotify does not fire for edits made
  on the host and seen through a bind mount, so without `DOTNET_USE_POLLING_FILE_WATCHER`
  hot reload silently does nothing.
- **HTTPS via the compose files** mounts a dev certificate from `~/.aspnet/https` and needs
  `CERT_PASSWORD` in `.env`. Plain HTTP on 8080 is simpler for local work.
