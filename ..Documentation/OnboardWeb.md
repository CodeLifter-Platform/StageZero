# Onboarding — Hosted / local .NET

Running StageZero directly on a machine with the .NET SDK.

## Prerequisites

| Need | Version | Check |
|---|---|---|
| .NET SDK | 10.0.100 or later 10.0.x feature band | `dotnet --version` |

`global.json` pins `10.0.100` with `rollForward: latestFeature`, so any newer 10.0 SDK
works and a 9.x SDK is refused with a clear error.

## Get the code, build, run

```bash
git clone https://github.com/CodeLifter-Platform/StageZero.git
cd StageZero
dotnet build StageZero.sln
dotnet run --project StageZero
```

**What you should see:** Serilog logs `Platform: Linux, Data Directory: …`, the database
path, and then `No users found. Please visit /setup to create your admin account`. The
default profile listens on `https://localhost:5001` and `http://localhost:5000`; visit
`/setup` to create the first admin: email, password, done. No verification code; StageZero
never sends email. Two optional boxes on that form (the CodeLifter newsletter, StageZero
update news) post to codelifter.net, which sends one confirmation email; leave them
unticked and nothing is sent. A "forgot password" code is written to this log as a banner
headed `STAGEZERO PASSWORD RESET CODE`; the startup log says so
([README.md](../README.md#configuration)).

The UI opens in the dark theme; the sun/moon button in the header switches to light, and
the choice is remembered by the browser.

## Test

```bash
dotnet test StageZero.Tests/StageZero.Tests.csproj
```

89 tests, no network: Cloudflare is faked at the service interfaces and codelifter.net
is a fake HTTP handler.

Outside a container, `DataPathService` resolves a platform-appropriate directory
(`~/.config/stagezero` on Linux) rather than `/app-data`.

## Package (the NuGet library)

`Lifted.BlazorAuth.Basic` is versioned and published by CI on pushes to `main` — nothing
to do by hand, and **no tags**. Local build/pack and the NuGet.org key setup are in
[NUGET_PUBLISHING.md](NUGET_PUBLISHING.md).

## Gotchas

- **`/setup` is the front door on a fresh install.** With no users, other pages are not
  useful, and it is easy to read that as the app being broken.
- **Data Protection keys live in the app data directory.** If you move or clear that
  directory, existing auth cookies and antiforgery tokens become invalid — expected, but it
  looks like a login bug.
- **A page reload logs you out.** Auth lives in the Blazor circuit, not a cookie; it is a
  known gap (LivingSpec), not a broken login.
- **Where the data went.** Outside a container the database is in the per-user data
  directory (`~/.config/stagezero`, `~/Library/Application Support/StageZero`, or
  `%APPDATA%\StageZero`), not in the repo. Delete that directory to start fresh.
- **Cloudflare, not YARP.** StageZero no longer tries to be its own edge; the
  `StageZero.ReverseProxy` project was deleted in favour of managing a Cloudflare Tunnel.
  Anything you find referring to `ProxyHost`, HSTS fields, or the disabled YARP routing
  provider is stale.
