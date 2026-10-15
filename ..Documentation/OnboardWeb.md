# Onboarding — Hosted / local .NET

Building and running StageZero from source on a machine with the .NET SDK and Node.js. It is
two parts: the ASP.NET Core API in `StageZero/`, and the React web app in `web/`, which the
API serves once it is built.

## Prerequisites

| Need | Version | Check |
|---|---|---|
| .NET SDK | 10.0.100 or later 10.0.x feature band | `dotnet --version` |
| Node.js | 22 or later (npm comes with it) | `node --version` |

`global.json` pins `10.0.100` with `rollForward: latestFeature`, so any newer 10.0 SDK
works and a 9.x SDK is refused with a clear error. The web app's dependencies are pinned
exactly in `web/package.json` and `web/package-lock.json`.

## Get the code, build, run

```bash
git clone https://github.com/CodeLifter-Platform/StageZero.git
cd StageZero
dotnet build StageZero.sln
(cd web && npm ci && npm run build)      # builds the web app into StageZero/wwwroot
dotnet run --project StageZero
```

**What you should see:** Serilog logs `Platform: Linux, Data Directory: …`, the database
path, and then `No users found. Open the app in a browser to create your admin account`.
The default profile listens on `https://localhost:5001` and `http://localhost:5000`; open
it and a fresh install goes straight to **Setup**: email, password, done. No verification
code; StageZero never sends email. Two optional boxes on that form (the CodeLifter
newsletter, StageZero update news) post to codelifter.net, which sends one confirmation
email; leave them unticked and nothing is sent. A "forgot password" code is written to this
log as a banner headed `STAGEZERO PASSWORD RESET CODE`; the startup log says so
([README.md](../README.md#configuration)).

The UI opens in the dark theme; the Light/Dark button in the top bar switches, and the
choice is remembered by the browser. A sign-in survives reloads and restarts (it is a
cookie), until you sign out, change the password, or seven days pass unused.

### Working on the web app

For hot reload, run the API and the Vite dev server side by side:

```bash
dotnet run --project StageZero           # terminal 1: API on https://localhost:5001
cd web && npm run dev                    # terminal 2: http://localhost:5173
```

Open http://localhost:5173. Vite proxies `/api` to the API (override the target with
`STAGEZERO_API=…`). In VS Code, the **StageZero (API + web)** launch configuration starts
both.

The UI is built from the CodeLifter Design System, vendored verbatim in `web/src/design`.
With the sibling `Platform-Design` repo checked out next to this one, `npm run design:sync`
refreshes the copy and `npm run design:check` reports drift.

## Test

```bash
dotnet test tests/StageZero.Tests
cd web && npm run lint && npm run typecheck && npm test
```

222 .NET tests and 57 web tests, no network. Most .NET tests host the real app in memory
(`WebApplicationFactory`, `tests/StageZero.Tests/Infrastructure/StageZeroApp.cs`) against a
throwaway data directory and call the API like the browser does
(`Infrastructure/TestApi.cs`), with every outbound `HttpClient` routed to `FakeHttp`, so
Cloudflare, the IP sources and codelifter.net are answered by the test and an unmatched
request fails it. The web tests render the app against the API faked with MSW.
`npm run test:coverage` adds a coverage summary.

Outside a container, `DataPathService` resolves a platform-appropriate directory
(`~/.config/stagezero` on Linux) rather than `/app-data`.

## Package

**The app:** `dotnet publish StageZero/StageZero.csproj -c Release -o out`, after
`npm run build` in `web/` (publish copies `StageZero/wwwroot`). The container image
(OnboardDocker.md) does both for you.

**The NuGet library:** `Lifted.BlazorAuth.Basic` is versioned and published by CI on
pushes to `main` — nothing to do by hand, and **no tags**. Local build/pack and the
NuGet.org key setup are in [NUGET_PUBLISHING.md](NUGET_PUBLISHING.md).

## Gotchas

- **"StageZero's web app has not been built."** `dotnet run` alone serves only the API;
  the page says how to build the web app. Run `npm run build` in `web/`, or use
  `npm run dev` and open :5173.
- **Setup is the front door on a fresh install.** With no users, every page goes to
  `/setup`, and it is easy to read that as the app being broken.
- **Data Protection keys live in the app data directory.** If you move or clear that
  directory, the session cookie can no longer be read and everyone is signed out, and the
  stored Cloudflare tokens become undecryptable. Expected, but it looks like a login bug.
- **A 400 saying "must send the X-Requested-With header".** Every non-GET API call needs
  `X-Requested-With: StageZero`. The web app sends it; a hand-written `curl` must too.
- **Dev certificate.** `dotnet run` uses the ASP.NET Core dev certificate for
  https://localhost:5001; `dotnet dev-certs https --trust` once if the browser complains.
  The Vite proxy accepts it without trusting it.
- **Where the data went.** Outside a container the database is in the per-user data
  directory (`~/.config/stagezero`, `~/Library/Application Support/StageZero`, or
  `%APPDATA%\StageZero`), not in the repo. Delete that directory to start fresh.
- **Cloudflare, not YARP.** StageZero no longer tries to be its own edge; the
  `StageZero.ReverseProxy` project was deleted in favour of managing a Cloudflare Tunnel.
  Anything you find referring to `ProxyHost`, HSTS fields, or the disabled YARP routing
  provider is stale.
