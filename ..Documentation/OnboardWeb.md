# Onboarding — Hosted / local .NET

Running StageZero directly on a machine with the .NET SDK.

## Prerequisites

| Need | Version | Check |
|---|---|---|
| .NET SDK | 10.x | `dotnet --version` |

This repo has **no `global.json`**, so the SDK version is not pinned — whatever .NET 10 SDK
you have is used. That is a gap, not a design choice.

## Get the code, build, run

```bash
git clone https://github.com/CodeLifter-Platform/StageZero.git
cd StageZero
dotnet build StageZero.sln
dotnet run --project StageZero
```

**What you should see:** Serilog logs the resolved platform and data directory, the database
path, and then `No users found. Please visit /setup to create your admin account`. Visit
`/setup` to create the first admin.

Outside a container, `DataPathService` resolves a platform-appropriate directory
(`~/.config/stagezero` on Linux) rather than `/app-data`.

## Package (the NuGet library)

`Lifted.BlazorAuth.Basic` is versioned from CI using `BASE_VERSION` + run number and
published on pushes to `main`. **Do not reintroduce a `tags:` trigger** — publishing on
hand-pushed tags is exactly what the platform versioning contract rules out, and this repo
was moved off it.

## Gotchas

- **`/setup` is the front door on a fresh install.** With no users, other pages are not
  useful, and it is easy to read that as the app being broken.
- **Data Protection keys live in the app data directory.** If you move or clear that
  directory, existing auth cookies and antiforgery tokens become invalid — expected, but it
  looks like a login bug.
- **Cloudflare, not YARP.** StageZero no longer tries to be its own edge; the
  `StageZero.ReverseProxy` project was deleted in favour of managing a Cloudflare Tunnel.
  Anything you find referring to `ProxyHost`, HSTS fields, or the disabled YARP routing
  provider is stale.
