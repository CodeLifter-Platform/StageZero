# StageZero — Living Spec

The current state of the application. Maintained alongside the code: a change that alters
what StageZero does updates this file in the same commit.

## What it is

StageZero is a **self-hosting control panel for Cloudflare**: an ASP.NET Core Web API with a
React web app that keeps
domains pointed at the right IP address (dynamic DNS), publishes local services to the
internet through a Cloudflare Tunnel, and puts a Cloudflare Access policy in front of every
hostname it publishes. It is for one operator running services at home or on a small
server, and it runs anywhere .NET 10 or Docker runs. The repo also publishes the
`Lifted.BlazorAuth.Basic` NuGet package, whose services provide the username/password
authentication the app itself uses (its Blazor components are no longer used by StageZero).

## Status

**Beta, in development.** The app follows the platform versioning contract: the version comes
from the git tags (`.github/scripts/next-version.sh`), every merge into `main` publishes the
next patch as `X.Y.Z-pre`, and RELEASE MINOR / RELEASE MAJOR (`promote.yml`) rebuild the
newest pre-release's commit as a release. What ships is the container image: linux/amd64 and
linux/arm64, pushed to `ghcr.io/codelifter-platform/stagezero` and mirrored to Docker Hub
when configured, signed with cosign (keyless) with a provenance attestation and SBOM. A
pre-release moves the `pre` tag; a release moves `X.Y`, `X` and `latest`. The NuGet library
is still on the retired scheme (*Known gaps*).

## Features

**Working today**

- **Dynamic DNS.** `IpMonitorBackgroundService` polls the public IP (every 180 s), and
  every check runs `DnsVerificationService`, which rewrites any auto-update record in
  Cloudflare that doesn't match — after a change, or after someone edits the record by
  hand. The update is a PATCH of the address alone, so the record's proxy (orange cloud),
  TTL, comment and tags are left as they are. Only **A** records are auto-updated
  (`DnsRecord.SupportsAutoUpdate`): the IP lookup finds the IPv4 address, and the add/edit
  dialogs, the Cloudflare import and the verifier all apply that rule. AAAA and CNAME
  records are tracked but never auto-updated. Each check lists every zone with tracked
  records once (all pages — Cloudflare pages at 500 records) and matches the records in
  memory, taking each record's current ID from the listing. Each provider can be switched
  off without stopping monitoring. A change is
  published on the singleton `IIpChangeNotifier`; `GET /api/ip/events` streams it to the
  browser as server-sent events (first event: the IP as it stands), which the top-bar chip
  and the IP Monitor page listen to (no polling). UI: **IP Monitor** (history) and **DNS Configuration**.
- **Public IP by consensus.** `PublicIpResolver` asks three independent services in
  parallel — Cloudflare's `1.1.1.1/cdn-cgi/trace`, ipify, and AWS `checkip` — with a 5-second
  timeout each. Only a public IPv4 address counts as an answer (an HTML captive-portal
  page, a private or reserved address, anything malformed is discarded). Two sources must
  agree for the IP to change; a lone answer can only confirm the last known IP. Otherwise
  the check is inconclusive: nothing is recorded and DNS is left alone.
- **IP history grows with changes, not time.** Each `IpCheck` row is a run: the check that
  first saw an address (`CheckedAt`), the last one to confirm it (`LastConfirmedAt`), and
  how many did (`Confirmations`). An unchanged check extends the current row; a change
  starts a new one. Upgrading folds the old one-row-per-check history into runs (a real
  month of 2,715 rows became 11, every check still counted).
- **Cloudflare Tunnel routes** (`Services/Tunnel/`). Connect an account, create or adopt a
  tunnel, then map hostnames to local services; `TunnelSyncService` pushes ingress rules and
  proxied CNAMEs to Cloudflare. The setup page prints the connector install command for
  macOS, Windows, Linux, and Docker. UI: **Tunnel Setup** and **Tunnel Routes**.
- **Cloudflare Access** (`Services/Access/`). Every new route gets an Access application,
  default mode `identity` (owner email). Modes: `identity`, `service_token`, `both`, `none`.
  Minted service-token secrets are shown once in a dialog and never logged or stored;
  `IAccessSecretSink` is the extension point for sending them to a vault. Missing token
  permissions are detected and named before anything is provisioned. Detail:
  [CLOUDFLARE_ACCESS_SETUP.md](CLOUDFLARE_ACCESS_SETUP.md).
- **Username/password authentication** via the in-repo `Lifted.BlazorAuth.Basic` library's
  services, carried over HTTP by `Api/AuthEndpoints.cs`. A sign-in issues an encrypted,
  HTTP-only, SameSite=Strict session cookie (`stagezero.session`, 7 days sliding, Data
  Protection key ring). **It survives a reload and a restart.** Every request re-reads the
  user and checks a stamp of their id, email and password hash (`Api/Session.cs`), so a
  deleted, deactivated or locked-out account, or a changed password or email, ends every
  other session at its next request. A user who owes account setup (an unverified email
  after a reset, or the one-time password from `reset-password`) is signed in but gets 403
  from everything except finishing it, on `/account-setup`.
  `/setup` creates the first admin from one form (email, password) when no users exist;
  there is no email verification step, because the first person to reach a fresh install
  is the operator and the address is a login, not a proof. **StageZero never sends
  email.** `/forgot-password` (and the change-password dialog's re-verification) write
  their 6-digit code to the server log as an unmissable banner
  (`Services/Auth/ServerLogCodeService.cs`, heading `STAGEZERO PASSWORD RESET CODE`), the
  pages say so, and the startup log says where a code will land. Whoever can read the log
  controls the server, which is who may reset the admin. When the logged code is out of
  reach, `dotnet StageZero.dll reset-password <email>` on the server prints a one-time
  password (the next sign-in must replace it) and lifts any lockout; deleting the `Users`
  row to bring `/setup` back is the last resort. The auth pages are reachable by direct URL
  and reload.
- **Brute-force limits** (`Lifted.BlazorAuth.Basic/Services/AuthThrottle.cs`, `AuthService`):
  codes come from `RandomNumberGenerator`, compare in constant time, and are void after
  5 wrong guesses; 5 wrong passwords in a row lock the account for 15 minutes (a success
  clears the count, and an unknown email pays the same BCrypt cost as a wrong password);
  and a per-address sliding window (10 per 15 minutes) throttles failed sign-ins and every
  reset request or wrong reset code. The address is the request's, after
  forwarded headers.
- **Optional signups on the setup form.** Two unchecked boxes (CodeLifter newsletter,
  StageZero update news) post once to codelifter.net's subscriptions API
  (`Services/CodeLifter/`, the library's `ISignupOptIn` extension point), which sends a
  single double-opt-in confirmation email. Best-effort: an unreachable site is a notice,
  never a failed setup. A blank `CodeLifter__SubscriptionsUrl` hides the boxes.
- **The API refuses anyone not signed in.** Every `/api/ip`, `/api/dns` and `/api/tunnel`
  route answers 401 without the cookie (tested route by route, and that no 401 body carries
  the IP). Only `/api/auth/*` and `/healthz` are open, and `GET /api/auth/session` says
  only whether setup is needed and who is signed in. Every non-GET API call must carry
  `X-Requested-With: StageZero` (`Api/ApiPipeline.cs`), which a cross-site form can't send.
- **The chrome needs a sign-in.** The public-IP chip and the sidebar render only inside the
  signed-in shell; the sign-in, setup and reset pages get a bare top bar (title and theme
  toggle), and never request the IP (tested).
- **Theme.** CodeLifter design system: dark (canonical ink) and light (warm paper) themes,
  StageZero teal accent, Inter + JetBrains Mono bundled in `web/public/fonts`. The top-bar
  toggle swaps a root attribute, which re-skins everything at once; the choice persists per
  browser (`localStorage`, `stagezero.theme`) and is applied before first paint.
- **Errors.** API errors are RFC 9457 problem details. A refused request (validation, not
  found, conflict, Cloudflare refusing a call: 400/404/409/502) carries a message written
  for the operator, which the web app shows as is. Outside Development, an unhandled
  exception is a 500 with a reference (the trace ID) and nothing from the exception; the
  web app shows a calm message with that reference.
- **Health.** `/healthz` (anonymous, status only): Unhealthy if the database can't be
  reached; Degraded — still HTTP 200 — when the public IP hasn't been confirmed for 15
  minutes (an upstream outage a restart wouldn't fix). The image's `HEALTHCHECK` runs
  `dotnet StageZero.dll healthcheck`, which probes the app's own port (from
  `ASPNETCORE_URLS` / `ASPNETCORE_HTTP_PORTS`, default 8080) — the runtime image has no curl.
- **No outside assets.** Every stylesheet, script and font is served by the app; pages make
  no request to a CDN, font service or script kit (checked by a web test; the vendored
  design system's Google Fonts import is deliberately not vendored).
- **Deep links.** Any path that isn't the API or a file gets the web app's `index.html`
  (uncached), so a reload or a bookmark lands on the right screen. An unknown `/api` path
  is a 404 problem, never HTML. A checkout run with `dotnet run` before the web app was
  built answers 503 with how to build it.
- **Serilog** structured logging to console and daily rolling files (31 kept). Debug in
  Development, Information elsewhere; `STAGEZERO_LOG_LEVEL` overrides. ASP.NET Core,
  EF Core and HttpClient log at Warning and above whatever the level, so
  per-request framework chatter never reaches the logs. Password-reset codes are logged at
  Warning, so they show at every level.

**Partial**

- **Home page feature cards are static copy**, not live status. They claim only what
  StageZero does: Cloudflare is the only DNS provider and the only tunnel.

**Removed**

- **The Blazor Server UI and MudBlazor are gone** (2026-10-15). The same screens are now
  the React app in `web/`, on the same URLs; the view models' logic moved to
  `Services/Dns/DnsConfigService.cs`, `Services/Tunnel/TunnelRouteService.cs` and
  `Services/Tunnel/TunnelSetupService.cs`. Two behaviours changed on the way: the Add
  Provider dialog's "enable updates" switch is honoured (it was ignored), and every
  password rule is 8 characters (the account-setup dialog accepted 6).

- **The YARP reverse proxy and Let's Encrypt handling are gone** (PR #7). StageZero no
  longer terminates TLS or routes traffic itself; Cloudflare's edge and `cloudflared` do.
  Anything referring to `ProxyHost`, HSTS fields, port forwarding, or certificate renewal is
  stale.

## Architecture

Three projects and a web app:

- **`StageZero/`** — the ASP.NET Core host. `Api/` holds the HTTP surface: thin minimal-API
  endpoint groups (`AuthEndpoints`, `IpEndpoints`, `DnsEndpoints`, `TunnelEndpoints`) that
  bind a request, call a service and shape the response; `Session.cs` (cookie auth and the
  per-request user check); `ApiPipeline.cs` (JSON conventions, the request-header guard,
  `no-store`, problem details); `SpaHosting.cs` (static files and the deep-link fallback).
  Business logic lives in `Services/`; data access in `DataAdapters/` as Reader/Writer
  pairs; EF Core over SQLite. A service throws `UserFacingException` for anything the
  operator should read; anything else is a 500 with a reference.
- **`web/`** — the React 18 + TypeScript (strict) app, built by Vite into
  `StageZero/wwwroot`. `src/api/client.ts` is the only way it talks to the API;
  `src/app/` holds the session, guards, shell and toasts; `src/pages/` the screens.
  UI is built only from the CodeLifter Design System, vendored verbatim in `src/design/`
  (`npm run design:sync` copies it from the sibling `Platform-Design` checkout, and
  `design:check` fails CI on drift) and imported through `src/ui.ts`.
- **`Lifted.BlazorAuth.Basic/`** — the auth library, published to NuGet. StageZero uses its
  `AuthService`, throttle, user store and opt-in extension point; `IAuthService.ResumeAsync`
  (added for this, with a default so other implementers don't break) makes a
  cookie-authenticated user current for one request.
- **`tests/StageZero.Tests/`** — xUnit. Most tests host the real app in memory
  (`WebApplicationFactory`) and call the API through a cookie-keeping client that sends the
  request header (`Infrastructure/TestApi.cs`); every outbound `HttpClient` is answered by a
  fake, and Cloudflare's tunnel and Access APIs can be swapped for in-memory accounts, so
  nothing reaches the network. The web app has its own Vitest suite (`web/src/**/*.test.ts*`)
  against the API faked with MSW.

Files worth opening first:

- `StageZero/Program.cs` — DI registrations, the pipeline, data directory, the
  `healthcheck`, `reset-password` and `copy-to-postgres` commands;
  `Data/DatabaseSettings.cs` picks SQLite or PostgreSQL; `Data/DatabaseInitializer.cs`
  applies the EF Core migrations (and adopts a pre-migration database once).
- `StageZero/Api/Session.cs` and `StageZero/Api/ApiPipeline.cs` — the trust boundary.
- `web/src/App.tsx`, `web/src/app/guards.tsx`, `web/src/app/Shell.tsx` — routes, who may
  see what, and the signed-in frame.
- `web/src/theme/stagezero.css` — the token dictionary port (see below).
- `StageZero/Services/Tunnel/TunnelSyncService.cs` and
  `StageZero/Services/Access/AccessProvisioningService.cs` — the two services that write to
  Cloudflare on route changes.

**Design system.** `web/src/theme/stagezero.css` is StageZero's port of
`Platform-Standards/design/tokens.md`. On top of the design system's own `--cl-*` tokens it
re-points the accent family to StageZero teal (`#14b8a6` dark, `#0f766e` light; accent text
`#2dd4bf` / `#115e59`; locked in `Platform-Standards/design/app-accents.md`) and the light
canvas to the harness's light-warm paper, which is recorded divergence #1 between the
harness and `Platform-Design`, left unresolved. It is the only file in `web/` that names a
colour (an ESLint rule and a test enforce it).

**Web stack exception.** The platform's web standard is Next.js on Vercel. StageZero has to
run on the operator's own network (it finds their public IP and drives `cloudflared`
there), so its web app is a Vite SPA served by its own API host, one container. Recorded in
`Platform-Standards/architecture/web.md`, alongside Singular's identical exception.

`DataPathService` resolves the app data directory and **detects a container**, returning
`/app-data` inside one and a platform directory otherwise.

## Data and state

Under the resolved app data directory (`/app-data` in a container; `~/.config/stagezero`
on Linux, `%APPDATA%\StageZero` on Windows, `~/Library/Application Support/StageZero` on
macOS): `stagezero.db` (SQLite), `logs/`, and the Data Protection keyring (`dp-keys/`;
an older install's `keys/` ring is copied in on first start, `Services/DataProtectionKeys.cs`).

The database is SQLite (`stagezero.db` in the data directory) unless
`STAGEZERO_DB_PROVIDER=postgres` and `STAGEZERO_DB_CONNECTION` name a PostgreSQL database
(`Data/DatabaseSettings.cs`); a misspelt provider or a missing connection string stops
startup. Each provider has its own migrations: SQLite's in `Data/Migrations/`, PostgreSQL's
in `Data/Migrations/Postgres/`, carried by `PostgresApplicationDbContext` so EF keeps them
apart, and the app still resolves `ApplicationDbContext` everywhere. On PostgreSQL every
`DateTime` is written as UTC (timestamptz refuses an unmarked one), and the data directory
is still required: it holds the logs and the Data Protection keys. `StageZero
copy-to-postgres` (`Services/Cli/CopyToPostgresCommand.cs`) moves an install: it migrates
the SQLite file, refuses a PostgreSQL database that already has data, copies every table
with its ids in one transaction, and moves the identity sequences past them; the SQLite
file is only read. The test suite runs on both: `STAGEZERO_TEST_POSTGRES` gives each
in-process app test its own throwaway PostgreSQL database, and CI's `api` job runs once per
provider.

The SQLite schema is EF Core migrations (`Data/Migrations/`), applied at startup; a database from
before migrations existed is adopted once (`DatabaseInitializer`, `LegacyDatabaseAdopter`)
and then migrated like any other. So is one migrated by the retired pre-#18 migration set
(a history naming none of today's migrations): it is adopted at the latest schema, since its
data already went through every current migration. Either way the original is kept beside it
as `stagezero.db.pre-migrations-<ts>.bak`. The Cloudflare DNS token and tunnel token are encrypted
with ASP.NET Data Protection (`Services/CloudflareTokenProtector.cs`) before they reach
SQLite — a plaintext token from an older install is encrypted on first start — and losing
the keyring makes them undecryptable. Access service-token client secrets are never persisted. The theme choice
and the sidebar's collapsed state live in the browser's `localStorage` (`stagezero.theme`,
`stagezero.sidebar`), not on the server. The session is a cookie holding the user id and a
credentials stamp, encrypted with the same key ring; no session state is kept server-side.

## External services

Cloudflare (DNS, Tunnel, Access), a public-IP lookup, codelifter.net (optional signups on
first run), NuGet.org and GitHub Packages for the library, and codelifter.net for release
notes. Canonical inventory: [`SERVICES.md`](../SERVICES.md).

## Platform matrix

| Target | Ships | Format | Verified |
|---|---|---|---|
| Container | ✅ | `StageZero/Dockerfile` (`web` → `build` → `release` stages, plus `debug`: `dotnet watch` + `vsdbg`; release non-root with `HEALTHCHECK`; plain HTTP on 8080), `docker-compose.yml` (release, :5100, optional `cloudflared` sidecar) and `docker-compose.debug.yml` (debug: API with hot reload on :5200 and the Vite dev server on :5173, source mounted, data kept apart from the release instance's) | ✅ macOS, 2026-10-15: both images built from clean; release came up healthy, served the app on a deep link, refused an anonymous API call, completed setup and sign-in over plain HTTP with the cookie honoured; debug served the app on :5200 and the Vite proxy on :5173, a `.cs` edit on the host was picked up by `dotnet watch` and a `.tsx` edit hot-reloaded in Vite, and the 57 web tests pass inside the web container. CI builds both images and validates both compose files on every push |
| Any .NET 10 host | ✅ | `dotnet run` + `npm run build` / published output | ✅ Linux, 2026-10-15: build (0 warnings), 222 xUnit + 57 Vitest tests, lint and strict typecheck clean, and a Chromium walkthrough of the built app: fresh install → setup (validation) → wrong password → sign in → reload keeps the session → IP Monitor, DNS (add-provider dialog), Tunnel Routes, route editor (owner-email default, mode switching), save refused before tunnel setup, Tunnel Setup, light theme persists across reload, sign out → deep link → sign in returns to it, phone width without horizontal scroll. Cloudflare itself was not reachable from the test machine, so no live DNS or tunnel write was made |
| NuGet | ✅ | `Lifted.BlazorAuth.Basic`, versioned from CI | ✅ local pack, 2026-09-26 |

Onboarding: [OnboardWeb.md](OnboardWeb.md) (build and run from source) and
[OnboardDocker.md](OnboardDocker.md). This repo is **public**, so its CI stays on
GitHub-hosted runners.

## Known gaps

- **The NuGet library is versioned on the retired scheme.** The app moved to tag-derived
  versions (`build.yml`, `promote.yml`), but `basic-auth-nuget-publish.yml` still computes
  `BASE_VERSION` + run number from repo variables, as this repo's `CLAUDE.md` records. Two
  schemes in one repo until the library moves too (`Platform-Standards/process/versioning-ci.md`).
- **The Docker Hub mirror is not configured yet.** Until `DOCKERHUB_USERNAME`,
  `DOCKERHUB_TOKEN` and `DOCKERHUB_IMAGE` are set, images go to GHCR only, with a warning.
  The GHCR package also has to be made public once, after its first push.
- **`NUGET_API_KEY` is not set.** The NuGet.org push is skipped with a warning; the package
  still reaches GitHub Packages. See [NUGET_PUBLISHING.md](NUGET_PUBLISHING.md).
- **The brand marks are still the pre-accent green.** `Assets/svg/*` draw the mark in
  `#34d399` — the platform's *success* status hue — and were never updated when the teal
  accent was locked. They need re-cutting in the icon design project.
- **Platform-Design has no `--cl-app-stagezero`.** The design system's per-app accent block
  doesn't list StageZero; the accent is locked only in the harness table for now.
- **There is no second factor.** The release audit decided against TOTP for this release
  (local-only sign-in on a home network is the accepted model for now). Sessions now live
  in a cookie, which the audit had also deferred; that was needed for an API anyway.
- **Not published anywhere yet.** CI (`build.yml`) builds and tests the API, the web app and
  the container on every push, but nothing releases the app or pushes an image (audit items
  1.5 and 1.6); the install path is clone and build.
- **The design system has no inline alert.** `web/src/components/Notice.tsx` composes Card
  + StatusDot for the info/warning/error notices the screens need. An `Alert` (or
  `Callout`) component should be added to Platform-Design and replace it. Three smaller
  design-system issues surfaced too, worked around in StageZero without editing the
  vendored files: `StatusDot` without a label renders as a sizeless inline span (Notice
  wraps it in a flex box); `Switch` accepts no label or `aria-label` (StageZero wraps each
  in a `<label>`); and `Input`'s accessible name includes its hint text, because the hint
  sits inside the `<label>`. All four are in `Platform-Standards/FOLLOWUPS.md`.
- **The library still ships Blazor components StageZero doesn't use.** The app references
  `Lifted.BlazorAuth.Basic` for its services only, which brings MudBlazor in transitively.
  Splitting the services into their own package is the clean fix; it changes the published
  package, so it is left for its own change.
- **The tunnel API token is typed on every setup step.** The wizard is stateless: the token
  travels in each request body (never a URL) until a tunnel is saved, then lives encrypted
  in the database. Same behaviour as before, now visible as API calls.
