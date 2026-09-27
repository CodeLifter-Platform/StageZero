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
email and a password, and that is the account. No verification code, no SMTP. The two
optional boxes (CodeLifter newsletter, StageZero update news) post to codelifter.net,
which sends one confirmation email; unticked, nothing is sent.

"Forgot password" still sends a 6-digit code:

- **With SMTP configured** (`Email__*` in `.env`; see [README.md](../README.md#configuration)),
  the code is emailed.
- **Without SMTP**, the page says the code went to the server log. Read it with
  `docker logs <container>` — the line is
  `SMTP is not configured (...), so the password reset code for you@example.com was written
  here instead of emailed. Password reset code: 123456`.

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
- **Email needs `.env` to reach the container.** The compose files pass it with `env_file`;
  a plain `docker run` needs `--env-file .env`. `.env` itself is excluded from the image by
  `.dockerignore`, on purpose. A relay without TLS (a mail sidecar, MailHog) needs
  `Email__UseStartTls=false`; the default STARTTLS handshake fails against it with
  `Server does not support secure connections`.
