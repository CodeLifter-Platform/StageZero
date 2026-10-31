# Onboarding — Docker

The intended way to run StageZero. Two compose files, each targeting a stage in
`StageZero/Dockerfile` (the stage names `release` and `debug` are a contract with them):

| | File | Open | Data |
|---|---|---|---|
| **Release** — the instance you keep running | `docker-compose.yml` | http://localhost:5100 | `STAGEZERO_DATA_DIR`, default `./.volumes/stagezero-data` |
| **Debug** — hot reload for the API and the web app, debugger attachable | `docker-compose.debug.yml` | http://localhost:5173 (web app) · http://localhost:5200 (API) | `./.volumes/stagezero-debug` |

Both listen on plain HTTP. Release is meant to sit behind a Cloudflare Tunnel, where
Cloudflare terminates TLS; opened directly on this machine it is just `http://localhost`.
There is no certificate to set up.

## Prerequisites

| Need | Version | Check |
|---|---|---|
| Docker | any recent | `docker info` |
| Docker Compose | v2 | `docker compose version` |

Nothing else: the image builds the web app with its own Node and the API with its own SDK.

## Release

```bash
git clone https://github.com/CodeLifter-Platform/StageZero.git
cd StageZero
docker compose up -d --build
```

Then open http://localhost:5100.

**Or run the published image** — no checkout to build, linux/amd64 and linux/arm64 (a
Raspberry Pi or an ARM NAS works). Put `STAGEZERO_IMAGE` in `.env` next to the compose file
and pull instead of building:

```bash
echo "STAGEZERO_IMAGE=ghcr.io/codelifter-platform/stagezero:latest" >> .env   # or :pre
docker compose pull && docker compose up -d
```

`latest` is the newest release someone cut with RELEASE MINOR/MAJOR; `pre` is the newest
merge into `main`; `0.1.0`-style tags never move. Verify a signature with
`cosign verify ghcr.io/codelifter-platform/stagezero:<tag> --certificate-identity-regexp
'^https://github.com/CodeLifter-Platform/StageZero/' --certificate-oidc-issuer
https://token.actions.githubusercontent.com`. `docker compose logs -f` follows the log, `docker compose
down` stops it; the data survives in the mounted directory.

**Where the data goes.** `/app-data` in the container holds the database, the logs and
the data-protection keys that decrypt the stored Cloudflare tokens. The compose file mounts
`STAGEZERO_DATA_DIR` there, default `./.volumes/stagezero-data` in the checkout. To keep it
where the host-run app would (`~/Library/Application Support/StageZero` on macOS,
`~/.config/stagezero` on Linux, `%APPDATA%\StageZero` on Windows), start it with the
wrapper instead, which sets the variable for you:

```bash
./docker-run.sh up          # macOS / Linux   (also: down, logs, restart, build)
.\docker-run.ps1 up         # Windows
```

Or set `STAGEZERO_DATA_DIR` in `.env`; compose reads it from there too.

**What you should see:** the log reports `Platform: Linux, Data Directory: /app-data`, then
`Database path: /app-data/stagezero.db`, then `No users found`, then one line saying that
password-reset codes are written to this log. The IP monitor and change-handler services
start. `/` answers 200 and `docker ps` shows the container `healthy` within a minute.

Go to `/setup` first — with no users, that is the only useful page. It asks for the admin
email and a password, and that is the account. No verification code; StageZero never sends
email. The two optional boxes (CodeLifter newsletter, StageZero update news) post to
codelifter.net, which sends one confirmation email; unticked, nothing is sent.

"Forgot password" writes a 6-digit code to the container log as a banner headed
`STAGEZERO PASSWORD RESET CODE`, with the account and the code on their own lines. See
**Forgotten password** below.

**Behind the tunnel.** Add a route pointing your hostname at `http://localhost:5100` (or at
`http://stagezero:8080` if cloudflared runs as the optional sidecar:
`docker compose --profile tunnel up -d` with `TUNNEL_TOKEN` in `.env`). Full steps:
[CLOUDFLARE_TUNNEL_SETUP.md](CLOUDFLARE_TUNNEL_SETUP.md).

**Reaching it from the LAN.** The port is published on every interface, so a phone on the
wifi or a cloudflared on another box can reach `http://<this-machine>:5100`.
`STAGEZERO_BIND=127.0.0.1` restricts it to this machine — except on a Docker engine that
runs in a VM with gvproxy port forwarding (Lima, Colima), where a guest-loopback port is
unreachable from the host.

Without compose:

```bash
docker build -f StageZero/Dockerfile --target release -t stagezero:local .
docker run -d -p 5100:8080 --env-file .env -v stagezero-data:/app-data stagezero:local
```

The build context is the **repo root**, not `StageZero/`: the API project references
`../Lifted.BlazorAuth.Basic` and the web app lives in `../web`.

### On PostgreSQL

`docker-compose.postgres.yml` adds a PostgreSQL 18 container (named volume
`stagezero-postgres`, no published port) and points the app at it:

```bash
echo "POSTGRES_PASSWORD=$(openssl rand -hex 24)" >> .env
docker compose -f docker-compose.yml -f docker-compose.postgres.yml up -d --build
```

The app's `/app-data` mount is still required: the Data Protection keys and the logs stay
there. To move an install that has been on SQLite, bring up the database alone, copy, then
start the app (the SQLite file is only read):

```bash
docker compose -f docker-compose.yml -f docker-compose.postgres.yml up -d postgres
docker compose -f docker-compose.yml -f docker-compose.postgres.yml run --rm stagezero copy-to-postgres
docker compose -f docker-compose.yml -f docker-compose.postgres.yml up -d
```

For a PostgreSQL server you already run, skip the add-on and set `STAGEZERO_DB_PROVIDER=postgres`
and `STAGEZERO_DB_CONNECTION` in `.env` — or leave `.env` alone and use the app's **Database**
page: it tests the connection, copies the SQLite data, saves the connection string encrypted
in `/app-data/database.json`, and stops the container, which `restart: unless-stopped` brings
straight back on PostgreSQL. The variables, when set, always win over that file, and the
page then only shows the database. `/app-data` stays mounted either way.

The Database page can also **create** a PostgreSQL container for you, which needs the Docker
socket in the StageZero container:

```bash
# Linux only: the socket's group, so the container's unprivileged user may use it
echo "DOCKER_GID=$(stat -c %g /var/run/docker.sock)" >> .env
docker compose -f docker-compose.yml -f docker-compose.provision.yml up -d
```

Read the header of `docker-compose.provision.yml` first: whoever holds the socket holds the
host, so use this only behind Cloudflare Access or on a machine nobody else reaches. The page
then creates `stagezero-postgres` (PostgreSQL 18) on the port you choose, joined to the
stack's network, with its data in `<STAGEZERO_DATA_DIR>/postgres` and a backups folder of
your choice (default `<STAGEZERO_DATA_DIR>/backups`), both bind mounts: `docker compose
down`, `down -v` and `docker volume prune` never touch them. It asks for two passwords, the
`postgres` superuser's and the `stagezero` role's, shows them once and stores neither:
record both. The container is not part of the compose project; `docker rm -f
stagezero-postgres` removes it (the folders stay).

## Debug

```bash
docker compose -f docker-compose.debug.yml up --build
```

Two containers come up:

- **`stagezero-debug`** — the API under `dotnet watch` on http://localhost:5200, with
  `StageZero/` and `Lifted.BlazorAuth.Basic/` mounted from the checkout. Save a `.cs` file
  and the app restarts (or hot-reloads) inside the container; the log shows it. It serves
  the web app as built into the image, so a deep link on :5200 works, but UI edits don't
  reach it — that's what the second container is for.
- **`stagezero-debug-web`** — the Vite dev server on http://localhost:5173, with `web/`
  mounted, proxying `/api` to the API container. Save a `.tsx` file and the browser
  updates. Its `node_modules` live in a volume (the host's hold macOS/Windows native
  binaries) and are installed from the lockfile on first start, and again only when the
  lockfile changes.

Its data is **separate from the release instance's** on purpose, in `./.volumes/stagezero-debug`
(`STAGEZERO_DEBUG_DATA_DIR` overrides): a debug build pointed at the real database would
start rewriting real DNS records within seconds. So the first visit is `/setup` again.

Changing a package reference, `global.json` or a `Directory.*.props` file needs
`up --build` again; so does anything in `web/package.json`. Build output (`bin/`, `obj/`)
stays in container volumes and never lands in the checkout.

**Breakpoints.** The image carries the VS Code debugger (`vsdbg`). In VS Code, run
**StageZero container: attach (debug)** and pick the process named `StageZero`. After
`dotnet watch` restarts the app (a rude edit), attach again. The source maps `/src` in the
container to the checkout.

**VS Code.** **StageZero container (debug)** and **StageZero container (release)** in Run
and Debug start the respective stack in the terminal and open the browser when it is
ready; stopping the session runs `docker compose down`. The same four operations are
Terminal → Run Task under `container: …`. (The non-container configurations —
**StageZero (API + web)** and **StageZero API only** — run the app on the host under the
.NET debugger; see [OnboardWeb.md](OnboardWeb.md).)

## Test

The test suites are not run inside the images; run them on the host
([OnboardWeb.md](OnboardWeb.md#test)), or in the debug stack's web container when the
host's Node is not 22: `docker compose -f docker-compose.debug.yml exec web npm test`.
CI builds both images on every push, validates both compose files, starts the release
image, and checks it turns healthy, serves the web app and refuses an anonymous API call.

## The stages

- **`web`** — builds the React app in `web/` with Node 22 (`npm ci`, `tsc`, `vite build`).
  Both stages below copy its output in.
- **`release`** — the published app on the ASP.NET runtime image, as the unprivileged user
  `$APP_UID` (1654), with a `HEALTHCHECK` that asks its own `/healthz`. What
  `docker-compose.yml` runs.
- **`debug`** — the SDK image with `dotnet watch run` over `/src`, a polling file watcher,
  the web app at `/web/wwwroot` (served via `ASPNETCORE_WEBROOT`, outside the source
  mount so the mount can't hide it), and `vsdbg`. Root, as hot reload needs to write
  build output. What `docker-compose.debug.yml` runs.

## Forgotten password

`/forgot-password` writes the code to the container log; read it with
`docker logs stagezero 2>&1 | grep -A6 "PASSWORD RESET CODE"` and enter it on
`/reset-password`. If the log is gone, reset from inside the container:
`docker exec -it stagezero dotnet StageZero.dll reset-password you@example.com` prints a
one-time password. As a last resort, stop the container and delete the admin row from the
database on the mounted volume (`sqlite3 <STAGEZERO_DATA_DIR>/stagezero.db "DELETE FROM
Users;"`); `/setup` returns on the next visit and every DNS and tunnel setting survives.
Full steps: [README.md](../README.md#configuration).

## Gotchas

- **`/app-data` must be a mounted volume.** `DataPathService` detects the container and puts
  the database, logs, *and* the Data Protection keys there. Without a volume you lose all
  three on restart — and losing the keys means every user is signed out and the stored
  Cloudflare tokens can't be decrypted.
- **The release container runs as a non-root user** (`$APP_UID`, 1654). A named volume
  created against this image is owned correctly; on Linux a bind mount, or a volume from
  an install that ran as root, needs its ownership fixed once or the database can't be
  written: `sudo chown -R 1654 <dir>` (Docker Desktop on macOS and Windows maps the
  ownership for you).
- **The build context is the repo root.** Building from inside `StageZero/` fails on the
  first `COPY`, because the auth library and the web app live one level up.
- **`STAGEZERO_HOME` in `.env` reaches the container too** and would point the app at a
  host path that doesn't exist there. Leave it out of `.env`; the container's data
  directory is `/app-data`, set by the volume mount.
- **The polling file watcher is required in `debug`.** inotify does not fire for edits made
  on the host and seen through a bind mount, so without `DOTNET_USE_POLLING_FILE_WATCHER`
  (and `STAGEZERO_WATCH_POLL` for Vite) hot reload silently does nothing.
- **Port already in use.** Release takes 5100, debug takes 5200 and 5173 — 5173 is also
  `npm run dev` on the host, so stop that first. `lsof -i :5173` names the holder. (Debug
  deliberately avoids 5000: on macOS, AirPlay Receiver listens there and answers every
  request with 403, which looks exactly like a broken container.)
- **`.env` only reaches the container if you pass it.** The compose files do so with
  `env_file`; a plain `docker run` needs `--env-file .env`. `.env` itself is excluded from
  the image by `.dockerignore`, on purpose. Nothing in it is required: the one thing it can
  change for the login flow is hiding the optional signup boxes
  (`CodeLifter__SubscriptionsUrl=`).
