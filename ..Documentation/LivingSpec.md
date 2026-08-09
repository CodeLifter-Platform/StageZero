# StageZero — Living Spec

The current state of the application. Maintained alongside the code: a change that alters
what StageZero does updates this file in the same commit.

## What it is

StageZero is a **dynamic-DNS tool**: a Blazor Server app that keeps domains pointed at the
right IP addresses automatically. It also publishes the `Lifted.BlazorAuth.Basic` NuGet
package, which provides the username/password authentication the app itself uses.

## Status

**Beta.** `BASE_VERSION` 0.9, `RELEASE_LEVEL` beta (both set 2026-08-09).

## Features

**Working today**

- **Dynamic DNS updates** driven by IP monitoring: `IpMonitorBackgroundService` watches for
  changes and `IpChangeHandlerService` reacts, updating DNS records through the configured
  provider (Cloudflare).
- **Cloudflare Tunnel management** (`Services/Tunnel/`): `CloudflareTunnelService`,
  `TunnelSyncService`, and `TunnelTokenProtector`, with tunnel routes and settings
  manageable from the UI (`Areas/TunnelManagement/`).
- **Let's Encrypt certificate handling** with an HTTP-01 challenge store and a renewal
  background service.
- **Username/password authentication** via the in-repo `Lifted.BlazorAuth.Basic` library,
  with a `/setup` flow that creates the first admin account when no users exist.
- **Data Protection keys persisted** to the app data directory, so auth cookies and
  antiforgery tokens survive a restart.
- **Serilog** structured logging to console and rolling files.

**Recently replaced**

- **The YARP reverse proxy is gone.** StageZero used to try to be its own edge: a
  `StageZero.ReverseProxy` project with a YARP dependency whose routing provider was still
  `.disabled`, a Certes/Let's Encrypt service, and a `ProxyHost` model full of SSL/HSTS
  fields — while `Program.cs` only ever registered a stub. That whole project is deleted in
  favour of managing a Cloudflare Tunnel.

## Architecture

A single ASP.NET Core project (`StageZero/`) using Blazor Server InteractiveServer mode,
MudBlazor for UI, and EF Core over SQLite. `Lifted.BlazorAuth.Basic/` is a separate library
project, published to NuGet and consumed by the app.

`DataPathService` resolves the app data directory and **detects a container**, returning
`/app-data` inside one and a platform-appropriate path otherwise. That is why nothing needs
to pass a connection string in Docker.

## Data and state

Under the resolved app data directory: `stagezero.db` (SQLite), `logs/`, and `keys/` for
Data Protection. In a container that whole directory is `/app-data` and must be a mounted
volume.

Tunnel tokens are protected with `TunnelTokenProtector` rather than stored raw.

## External services

Cloudflare (DNS and Tunnel), Let's Encrypt, and NuGet.org for package publishing.
Canonical inventory: [`SERVICES.md`](../SERVICES.md).

## Platform matrix

| Target | Ships | Format |
|---|---|---|
| Container | ✅ | `Dockerfile` with `debug` and `release` stages |
| Any .NET 10 host | ✅ | `dotnet run` / published output |
| NuGet | ✅ | `Lifted.BlazorAuth.Basic`, versioned from CI |

This repo is **public**, so its CI stays on GitHub-hosted runners; the self-hosted
`macbook` rule applies to private repos only.

## Known gaps

- **`NUGET_API_KEY` is not set.** The NuGet.org push is skipped with a warning rather than
  failing the build, per the platform's sign-when-present rule. The package still publishes
  to GitHub Packages. Tracked in `Platform-Standards/FOLLOWUPS.md`.
- **No `global.json`, `Directory.Build.props`, or `Directory.Packages.props`**, so the SDK
  and package versions are not centrally pinned. The Dockerfile's restore layer has a
  comment marking where they go when they land.
- **`CLOUDFLARE_TUNNEL_SETUP.md` and `GITHUB_ACTIONS_SETUP.md` remain at the repo root.**
  Under the documentation standard they belong here; each is referenced by several files,
  so moving them is a separate mechanical change. `DOCKER_SETUP.md` had no references and
  has moved in.
