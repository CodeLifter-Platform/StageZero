# StageZero

**Self-hosting control panel for Cloudflare DNS and Cloudflare Tunnels**

A React web app on an ASP.NET Core Web API that keeps your domains pointed at the right IP
addresses and publishes your local services to the internet through a Cloudflare
Tunnel — no port forwarding, no certificate management, no public IP required.

Three features, one Cloudflare API token:

- **Dynamic DNS** — monitors your public IP and updates Cloudflare DNS records when it changes.
- **Tunnel Routes** — maps public hostnames to services on your network by pushing
  ingress rules and proxied CNAMEs to Cloudflare. See
  [..Documentation/CLOUDFLARE_TUNNEL_SETUP.md](..Documentation/CLOUDFLARE_TUNNEL_SETUP.md).
- **Access (Zero Trust)** — puts an authentication policy in front of every hostname it
  publishes, so a tunnel does not mean an open door. See
  [..Documentation/CLOUDFLARE_ACCESS_SETUP.md](..Documentation/CLOUDFLARE_ACCESS_SETUP.md).

## Tech Stack

- **API:** .NET 10 / ASP.NET Core minimal APIs, Entity Framework Core on SQLite (default) or
  PostgreSQL, Serilog,
  a cookie session over the in-repo `Lifted.BlazorAuth.Basic` services
- **Web app:** React 18 + TypeScript (strict), built with Vite, served by the API from
  `wwwroot`; UI from the CodeLifter Design System (`Platform-Design`), vendored in
  `web/src/design`
- **Tests:** xUnit hosting the real app in memory; Vitest + Testing Library + MSW

## Getting Started

### Prerequisites

- .NET 10.0 SDK
- Node.js 22 (for the web app)
- Docker & Docker Compose (for containerized development)

### Local Development

1. **Copy environment file:**
   ```bash
   cp .env.example .env
   ```

2. **Run the API** (https://localhost:5001):
   ```bash
   cd StageZero
   dotnet run
   ```

3. **Run the web app** with hot reload, in a second terminal:
   ```bash
   cd web
   npm ci
   npm run dev
   ```

4. **Open** http://localhost:5173. Vite proxies `/api` to the API.

To have the API serve the web app itself (as it does in production), build it once with
`npm run build` in `web/`; the output lands in `StageZero/wwwroot`, and
https://localhost:5001 then serves the app directly.

### In a container

Nothing but Docker is needed on the host. Two compose files:

```bash
docker compose up -d --build                        # release: http://localhost:5100
docker compose -f docker-compose.debug.yml up       # debug:   http://localhost:5173 (web, hot reload)
                                                    #          http://localhost:5200 (API, dotnet watch)
```

Release is the instance you keep running; its data goes to `STAGEZERO_DATA_DIR` (default
`./.volumes/stagezero-data`, or the platform's app-data directory via `./docker-run.sh up`
/ `.\docker-run.ps1 up`). Debug mounts the source into the containers, restarts the API
on a `.cs` save and hot-reloads the UI on a `.tsx` save, keeps its own data apart from the
release instance's, and carries the VS Code debugger. Both run plain HTTP; Cloudflare
terminates TLS when the release instance sits behind a tunnel
([..Documentation/CLOUDFLARE_TUNNEL_SETUP.md](..Documentation/CLOUDFLARE_TUNNEL_SETUP.md)).
Details, VS Code configurations and gotchas: [..Documentation/OnboardDocker.md](..Documentation/OnboardDocker.md).

## Project Structure

```
StageZero/
├── StageZero/                 # ASP.NET Core host: Web API + serves the built web app
│   ├── Api/                   # Endpoints, cookie session, error shape, SPA hosting
│   ├── Data/                  # DbContext, migrations
│   ├── DataAdapters/          # Data access (Readers/Writers)
│   ├── Models/                # Domain entities
│   ├── Services/              # Business logic
│   │   ├── Access/                # Cloudflare Access apps, policies, service tokens
│   │   ├── Dns/                   # Cloudflare DNS, DDNS updates, the DNS screen's rules
│   │   ├── IpMonitoring/          # Public IP polling
│   │   └── Tunnel/                # Cloudflare Tunnel API, route sync, setup and routes
│   └── wwwroot/               # Build output of web/ (not in git)
├── web/                       # React + TypeScript web app (Vite)
│   ├── src/design/            # CodeLifter Design System, vendored verbatim
│   ├── src/theme/             # StageZero token layer (teal accent, warm light canvas)
│   ├── src/api/               # Typed API client
│   └── src/pages/             # Screens
├── Lifted.BlazorAuth.Basic/   # Auth library, published to NuGet
├── tests/StageZero.Tests/     # xUnit: API, services, persistence, auth
├── docker-compose.yml         # Release: the instance you keep running (+ optional cloudflared sidecar)
├── docker-compose.debug.yml   # Debug: dotnet watch + Vite dev server in containers
├── .env.example
└── StageZero.sln
```

## Architecture

- **Web app** (`web/`) — screens and their state only. It talks to the API through one
  typed client (`web/src/api/client.ts`) and holds no business rules the server doesn't
  also enforce.
- **API** (`StageZero/Api/`) — thin endpoints: bind the request, call a service, shape the
  response. Every operator route requires the session cookie; errors are problem details
  whose message is written for the operator.
- **Services** — all business logic, unchanged by the move off Blazor.
- **DataAdapters** — focused data access (Reader/Writer pattern).

## Configuration

### Environment Variables

The application uses a `.env` file for configuration. Copy `.env.example` to `.env` and configure as needed:

```bash
cp .env.example .env
```

**First run:** open the app (it goes to `/setup` on a fresh install), enter an email and a password, and that is the admin
account. No verification code; StageZero never sends email. The form's **Register this
install with codelifter.net** box (ticked by default) sends the admin email and StageZero's
version to codelifter.net so CodeLifter knows who runs it; it signs you up for nothing and
sends no email. The CodeLifter newsletter and StageZero update emails are separate, optional
choices on **Settings** (click your email), each with its own confirmation email. To switch
all of it off (a fork, an air-gapped install), set `CodeLifter__SubscriptionsUrl=` to blank in `.env`.

**Password reset without email:** StageZero does not send email, on purpose. The only
flow that ever needed it, "forgot password", writes its code to the server log instead,
in a banner headed `STAGEZERO PASSWORD RESET CODE`. Whoever can read the log controls the
server, and that is exactly who may reset the admin password.

**Forgotten admin password:** the reset is "prove you control the server", the same way
Jenkins or Vaultwarden do it. Three steps:

1. On `/forgot-password`, enter the admin email. The page says the code went to the
   server log.
2. Read the code from the log:
   ```bash
   docker logs stagezero 2>&1 | grep -A6 "PASSWORD RESET CODE"           # Docker
   grep -rA6 "PASSWORD RESET CODE" ~/.config/stagezero/logs/               # Linux host
   ```
3. Enter it on `/reset-password` with the new password. Codes expire after 15 minutes;
   request another if it lapsed.

**From the server** (the log is gone): `reset-password` prints a one-time password, lifts
any lockout, and makes the next sign-in choose a new password:

```bash
dotnet StageZero.dll reset-password you@example.com                                # published app
docker exec -it stagezero dotnet StageZero.dll reset-password you@example.com   # Docker
```

**Sign-in limits:** 5 wrong passwords lock the account for 15 minutes, a reset code is void
after 5 wrong guesses, and one address gets 10 failed sign-ins or reset requests per 15
minutes. `reset-password` clears a lockout too.

**Last resort** (the address itself is forgotten): remove the admin row and `/setup` comes
back on the next visit. DNS records, tunnel routes and Access settings are
in other tables and are untouched. With the app stopped and `sqlite3` on the host:

```bash
sqlite3 <data-dir>/stagezero.db "DELETE FROM Users;"
```

where `<data-dir>` is the mounted `/app-data` volume in Docker, `~/.config/stagezero` on
Linux, `~/Library/Application Support/StageZero` on macOS, or `%APPDATA%\StageZero` on
Windows. Deleting `stagezero.db` itself also works but loses every DNS and tunnel setting.

**Log level:** Information by default (Debug in Development). Set `STAGEZERO_LOG_LEVEL`
(`Debug`, `Information`, `Warning`, `Error`) to change it. Logs go to the console and to
`logs/` in the data directory, one file a day, 31 kept.

### DNS Provider Control

Each DNS provider (Cloudflare, etc.) can be individually enabled or disabled for updates:

- **New providers** start with updates enabled
- **Any time**: Use the toggle switch in the provider card header to enable/disable updates

When a provider is disabled:
- ✅ IP monitoring continues normally
- ✅ DNS records are still visible in the UI
- ❌ NO automatic DNS updates will be made for that provider

This is useful for:
- Testing without affecting production DNS
- Temporarily pausing updates for specific providers
- Development without valid DNS credentials

### Cloudflare API Token

One token drives all three features. Create it at **My Profile → API Tokens**, then save it
on StageZero's **Settings** page (click your email at the top right), which lists the same
permissions and checks the token has each one:

| Scope | Permission | Needed for |
|---|---|---|
| Zone | Zone → Read | Listing zones |
| Zone | DNS → Edit | Dynamic DNS and tunnel CNAMEs |
| Account | Cloudflare Tunnel → Edit | Tunnel routes |
| Account | Access: Apps and Policies → Edit | Access, any mode except `none` |
| Account | Access: Service Tokens → Edit | Access, `service_token` and `both` modes |
| Account | Access: Organizations, Identity Providers, and Groups → Read | Optional: listing identity providers |

The Access permissions are **account-scoped**, unlike the zone-scoped DNS permissions — a
token that writes DNS records fine may still have no Access rights at all. Step-by-step
creation, rotation, and the errors each missing permission causes:
[..Documentation/CLOUDFLARE_TOKENS.md](..Documentation/CLOUDFLARE_TOKENS.md).

The token is encrypted with ASP.NET Data Protection before storage. The
keyring lives in the app data directory (`/app-data/dp-keys` in Docker), which
must be on a persistent volume — otherwise the token can't be decrypted after a
restart.

### Cloudflare Access

Every hostname StageZero publishes is protected by Access by default. Each route
picks one of four modes:

| Mode | Who gets in |
|---|---|
| `identity` | People signing in through an identity provider, matched by email or email domain |
| `service_token` | Machines presenting a Cloudflare service token |
| `both` | Either |
| `none` | Everyone — no Access application at all |

New hostnames default to `identity`, allowing the owner email (the
`Access.OwnerEmail` setting, or the first account created during setup). Choosing
`none` is explicit and the UI marks those routes as public.

When StageZero mints a service token, **Cloudflare returns the client secret only
once**. It is shown in a dialog that must be acknowledged before it closes, and is
never logged, stored, or written to disk. Implement `IAccessSecretSink` to also
route it into a vault.

Full details — modes, permissions, idempotency, teardown rules and the rollback
behaviour when Access setup fails — are in
[..Documentation/CLOUDFLARE_ACCESS_SETUP.md](..Documentation/CLOUDFLARE_ACCESS_SETUP.md).

### VS Code Debugging

The project includes VS Code launch configuration with `.env` file support. To debug:

1. Open the project in VS Code
2. Pick **StageZero (API + web)** in the "Run and Debug" panel and press `F5`
3. It starts the API under the debugger and the Vite dev server, and opens http://localhost:5173

The `.env` file will be automatically loaded when debugging.

### Layout and theme
- Top bar with the current public IP (signed-in only), the light/dark toggle and the account
- Collapsible sidebar navigation (the design system's `Sidebar`, with an icon rail)
- CodeLifter design system: dark and light themes, StageZero teal accent, Inter + JetBrains
  Mono bundled with the app. Components come from `Platform-Design`, vendored verbatim in
  `web/src/design` (`npm run design:sync` / `design:check`); colors come only from
  `web/src/theme/stagezero.css`.

### Database
**SQLite** by default, created automatically as `stagezero.db` in the app data directory
(`/app-data` in a container, the per-user config directory otherwise). Nothing to set up.

**PostgreSQL** instead, on a server that runs **outside** StageZero's stack, so nothing done
to StageZero's container (a `down -v`, a recreate, an upgrade) can touch the data. The
platform's Infra stack is the intended one (`Infra/tools/create-app-db.sh prod stagezero`
makes the role and database). Choose it in the web app, either on the first-run page
(*Keep StageZero's data in PostgreSQL*) or later in **Settings → Database**: test the
connection, then **Copy and switch**. StageZero copies everything into the database (which
must be empty, so nothing is ever overwritten), saves the choice encrypted in
`database.json` in the data directory, and restarts onto it. From a container, a server on
the same machine is `host.docker.internal`. Switching runs one way, and the SQLite file is
kept as it was.

Or set it in the environment, which wins over the web app's choice and locks the card:

```bash
STAGEZERO_DB_PROVIDER=postgres
STAGEZERO_DB_CONNECTION="Host=db;Database=stagezero;Username=stagezero;Password=..."
```

With variables, move existing data with `dotnet StageZero.dll copy-to-postgres` once (in
Docker: `docker compose run --rm stagezero copy-to-postgres`), then restart. Either way the
data directory is still needed: it holds the logs and the keys that decrypt the stored
Cloudflare token and the saved connection, so back it up with the database.
`docker-compose.postgres.yml` runs a database inside StageZero's own stack; it is for CI and
trying PostgreSQL out, not for data you keep.

## Documentation

Everything beyond this README lives in [`..Documentation/`](..Documentation/) (a hidden
folder — `ls -a`):

| Doc | For |
|---|---|
| [LivingSpec.md](..Documentation/LivingSpec.md) | What StageZero does today, how it is built, and its known gaps |
| [USAGE_GUIDE.md](..Documentation/USAGE_GUIDE.md) | Using the app end to end: first run, sign-in and reset, DNS, IP monitor, tunnel, routes and Access, theme, container |
| [CLOUDFLARE_TOKENS.md](..Documentation/CLOUDFLARE_TOKENS.md) | Every Cloudflare credential: token permissions per feature, creating, entering and rotating it, the errors a missing permission causes |
| [OnboardWeb.md](..Documentation/OnboardWeb.md) | Build, run, and test on a machine with the .NET SDK |
| [OnboardDocker.md](..Documentation/OnboardDocker.md) | Run it in Docker: the release and debug containers, VS Code, data persistence |
| [CLOUDFLARE_TUNNEL_SETUP.md](..Documentation/CLOUDFLARE_TUNNEL_SETUP.md) | Publishing hostnames through a Cloudflare Tunnel |
| [CLOUDFLARE_ACCESS_SETUP.md](..Documentation/CLOUDFLARE_ACCESS_SETUP.md) | Access modes, permissions, service tokens |
| [NUGET_PUBLISHING.md](..Documentation/NUGET_PUBLISHING.md) | How `Lifted.BlazorAuth.Basic` is versioned and published |

## Tests

```bash
dotnet test tests/StageZero.Tests          # API, services, persistence, auth
cd web && npm test                          # web app (Vitest)
cd web && npm run lint && npm run typecheck
```

Cloudflare is faked at the service interfaces and the API is faked with MSW in the web
tests, so neither suite makes network calls. CI (`.github/workflows/build.yml`) also builds
both container images and checks the release one comes up healthy.

## License

[MIT](LICENSE) © CodeLifter LLC

## Downloads

| Platform | Formats | Get it |
|---|---|---|
| **Container** | `linux/amd64`, `linux/arm64`, signed (cosign) | `docker pull ghcr.io/codelifter-platform/stagezero` (Docker Hub mirror once configured) · [build from source](..Documentation/OnboardDocker.md) |
| **Any .NET 10 host** | `dotnet run` / published output | [Releases](https://github.com/CodeLifter-Platform/StageZero/releases) |
| **NuGet** | `Lifted.BlazorAuth.Basic` | [nuget.org](https://www.nuget.org/packages/Lifted.BlazorAuth.Basic) |

> StageZero is a server application — there is nothing to install on a desktop. The repo also
> publishes the `Lifted.BlazorAuth.Basic` package; see `..Documentation/NUGET_PUBLISHING.md`.

## Releases

Cut deliberately with the **RELEASE MINOR** / **RELEASE MAJOR** buttons in Actions. Each one
rebuilds a tested pre-release's commit with the release version compiled in, and moves the
image's `X.Y`, `X` and `latest` tags.

<!-- releases:insert -->
| Version | Date | Downloads | Rebuilt from | Notes |
|---|---|---|---|---|

## Pre-releases

Produced automatically by every merge into `main`: patch + 1 from the git tags, published as
`ghcr.io/codelifter-platform/stagezero:<version>` and `:pre`, marked as a prerelease on
GitHub. Versions come from the tags alone (`.github/scripts/next-version.sh`); nothing is
kept in a repo variable. The NuGet library is versioned separately, in
its own `basicauth-v*` tags, and its releases are not listed here
(`..Documentation/NUGET_PUBLISHING.md`).

<!-- prereleases:insert -->
| Version | Date | Downloads | Commit | Notes |
|---|---|---|---|---|
