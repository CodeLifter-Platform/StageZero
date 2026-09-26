# StageZero — Living Spec

The current state of the application. Maintained alongside the code: a change that alters
what StageZero does updates this file in the same commit.

## What it is

StageZero is a **self-hosting control panel for Cloudflare**: a Blazor Server app that keeps
domains pointed at the right IP address (dynamic DNS), publishes local services to the
internet through a Cloudflare Tunnel, and puts a Cloudflare Access policy in front of every
hostname it publishes. It is for one operator running services at home or on a small
server, and it runs anywhere .NET 10 or Docker runs. The repo also publishes the
`Lifted.BlazorAuth.Basic` NuGet package, which provides the username/password
authentication the app itself uses.

## Status

**Beta, in development.** Versions are `BASE_VERSION` (0.9) + CI run number with
`RELEASE_LEVEL=beta`, so every merge into `main` is a pre-release — see *Known gaps* for the
versioning migration.

## Features

**Working today**

- **Dynamic DNS.** `IpMonitorBackgroundService` polls the public IP (every 180 s);
  `IpChangeHandlerService` reacts to a change and `DnsUpdateService` rewrites every
  auto-update record through the Cloudflare DNS API. Each provider can be switched off
  without stopping monitoring. `DnsVerificationService` re-checks records against the
  current IP on each poll. UI: **IP Monitor** (history) and **DNS Configuration**.
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
- **Username/password authentication** via the in-repo `Lifted.BlazorAuth.Basic` library.
  `/setup` creates the first admin (email → verification code → password) when no users
  exist. With SMTP unconfigured, the verification code is written to the log.
- **Theme.** CodeLifter design system: dark (canonical ink) and light (warm paper) themes,
  StageZero teal accent, Inter + JetBrains Mono bundled in `wwwroot/fonts`. The header
  toggle swaps the whole UI and the choice persists per browser (`localStorage`).
- **Serilog** structured logging to console and rolling files.

**Partial**

- **Sign-in does not survive a page reload.** `AuthService` holds the current user in a
  scoped service, which in Blazor Server means per circuit; a reload starts a new circuit
  and lands on `/login`.
- **Home page feature cards are static copy**, not live status.

**Removed**

- **The YARP reverse proxy and Let's Encrypt handling are gone** (PR #7). StageZero no
  longer terminates TLS or routes traffic itself; Cloudflare's edge and `cloudflared` do.
  Anything referring to `ProxyHost`, HSTS fields, port forwarding, or certificate renewal is
  stale.

## Architecture

One ASP.NET Core project (`StageZero/`): Blazor Server in InteractiveServer mode, MudBlazor
UI, EF Core over SQLite, MVVM (views bind to view models; business logic lives in
`Services/`; data access in `DataAdapters/` as Reader/Writer pairs).
`Lifted.BlazorAuth.Basic/` is a separate library, published to NuGet and referenced by the
app. `StageZero.Tests/` is xUnit, with Cloudflare faked at the service interfaces.

Files worth opening first:

- `StageZero/Program.cs` — DI registrations, data directory, database creation.
- `StageZero/Application/Layout/MainLayout.razor` + `AppVM.cs` — shell, nav, theme toggle.
- `StageZero/Application/Theme/StageZeroTheme.cs` — the token dictionary port (see below).
- `StageZero/Services/Tunnel/TunnelSyncService.cs` and
  `StageZero/Services/Access/AccessProvisioningService.cs` — the two services that write to
  Cloudflare on route changes.

**Design system.** `StageZeroTheme.cs` is StageZero's port of
`Platform-Standards/design/tokens.md`: the dark and light values are copied verbatim, the
accent family is StageZero teal (`#14b8a6` dark, `#0f766e` light, locked in
`Platform-Standards/design/app-accents.md`), and MudBlazor's palette slots are mapped onto
those roles. Views use `Color.*` and `--mud-palette-*` only; `wwwroot/css/app.css` holds
the `@font-face` rules and the `sz-mono` / `sz-on-accent` classes and has no raw colors.

`DataPathService` resolves the app data directory and **detects a container**, returning
`/app-data` inside one and a platform directory otherwise.

## Data and state

Under the resolved app data directory (`/app-data` in a container; `~/.config/stagezero`
on Linux, `%APPDATA%\StageZero` on Windows, `~/Library/Application Support/StageZero` on
macOS): `stagezero.db` (SQLite), `logs/`, and the Data Protection keyring.

The Cloudflare API token and tunnel token are encrypted with ASP.NET Data Protection
(`TunnelTokenProtector`) before they reach SQLite; losing the keyring makes them
undecryptable. Access service-token client secrets are never persisted. The theme choice
lives in the browser's `localStorage` (`stagezero.theme`), not on the server.

## External services

Cloudflare (DNS, Tunnel, Access), a public-IP lookup, SMTP (optional, for verification
codes), NuGet.org and GitHub Packages for the library, and codelifter.net for release
notes. Canonical inventory: [`SERVICES.md`](../SERVICES.md).

## Platform matrix

| Target | Ships | Format | Verified |
|---|---|---|---|
| Container | ✅ | `StageZero/Dockerfile` (`debug` / `release` stages), three compose files | Assumed — not built in the 2026-09-26 docs pass (no Docker daemon) |
| Any .NET 10 host | ✅ | `dotnet run` / published output | ✅ Linux, 2026-09-26: build, 72 tests, run, setup, both themes |
| NuGet | ✅ | `Lifted.BlazorAuth.Basic`, versioned from CI | ✅ local pack, 2026-09-26 |

Onboarding: [OnboardWeb.md](OnboardWeb.md) (run on a .NET host) and
[OnboardDocker.md](OnboardDocker.md). This repo is **public**, so its CI stays on
GitHub-hosted runners.

## Known gaps

- **Versioning is on the retired scheme.** `basic-auth-nuget-publish.yml` still computes
  `BASE_VERSION` + run number from repo variables; the platform contract is tag-derived
  versioning with `release-minor.yml` / `release-major.yml` / `promote.yml`
  (`Platform-Standards/process/versioning-ci.md`). The conformance `version` check fails.
- **No `Directory.Build.props` / `Directory.Packages.props`.** `global.json` pins the SDK
  (10.0.100, `latestFeature`), but package versions are per-csproj and warnings are not
  errors. The conformance `dotnet` check fails; tracked in
  `Platform-Standards/FOLLOWUPS.md`.
- **`NUGET_API_KEY` is not set.** The NuGet.org push is skipped with a warning; the package
  still reaches GitHub Packages. See [NUGET_PUBLISHING.md](NUGET_PUBLISHING.md).
- **The brand marks are still the pre-accent green.** `Assets/svg/*` draw the mark in
  `#34d399` — the platform's *success* status hue — and were never updated when the teal
  accent was locked. They need re-cutting in the icon design project.
- **Platform-Design has no `--cl-app-stagezero`.** The design system's per-app accent block
  doesn't list StageZero; the accent is locked only in the harness table for now.
- **`src/Quip/Quip.csproj` is a stray template project**, not in the solution and not
  built. It can be deleted.
- **Auth is per circuit** (see *Partial*).
