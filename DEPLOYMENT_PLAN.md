# StageZero Deployment Plan

This document is the strategic plan for getting StageZero into production on a self-hosted Windows machine. It's the companion to [PHASE_A_DEPLOYMENT.md](PHASE_A_DEPLOYMENT.md), which is the operational host-side checklist.

**If you're a fresh Claude session picking this up — read this file end to end before doing anything. It tells you what was decided, what's already done, and what comes next. The [Status snapshot](#status-snapshot) section tells you exactly where to start.**

---

## Goal

Get **StageZero** — this .NET 10 Blazor Server app — running publicly at **https://stagezero.codelifter.net** from a Docker container on the owner's Windows machine, with no router port forwarding and no public IP requirement.

The owner controls `codelifter.net` (already on Cloudflare).

## Strategic direction

**Cloudflare Tunnel** is the edge layer for both routing and TLS. `cloudflared` runs as a Windows service, holds an outbound connection to Cloudflare, and routes inbound public requests to local services on the LAN. Cloudflare terminates TLS at its edge; the container speaks plain HTTP.

This replaces three things the repo originally planned for:

| Originally planned | Replaced by |
|---|---|
| Router port-forward of 80/443 | Outbound tunnel; no inbound ports |
| Origin TLS cert (Let's Encrypt via Certes) | Cloudflare edge cert |
| In-app YARP reverse proxy for subdomain routing | cloudflared ingress rules (Phase A) → API-managed tunnel from StageZero UI (Phase B) |

Net effect: the `StageZero.ReverseProxy` project — a stub today ([Program.cs:129](StageZero/Program.cs:129) registers `StubProxyConfigurationService`) — gets deleted in Phase B and a much smaller `CloudflareTunnelService` takes its place. The data model (`ProxyHost` → `TunnelRoute`) is trimmed but largely reused. The two Razor management pages are reused after stripping the SSL/HSTS fields that Cloudflare now handles.

## Two phases

- **Phase A — Deploy today.** Get the app live behind a hand-edited `cloudflared` `config.yml` on the Windows host. Small surface area, fast to verify end-to-end. The `config.yml` is intentionally throwaway.
- **Phase B — Refactor to API-managed tunnel.** Delete `StageZero.ReverseProxy`. Fold a trimmed `TunnelRoute` model and the management UI into the main app. Build `CloudflareTunnelService` that pushes ingress rules to Cloudflare's remotely-managed tunnel API. Cut over `cloudflared` from file-config to token-based remote config — same tunnel ID, no DNS change, ~30s downtime. After this, adding a new subdomain is a form submission in the StageZero UI.

---

## Status snapshot

> Update this section as work progresses.

- **Phase A code changes:** ✅ Done. See commit on branch `claude/eager-gagarin-9eedc4`. Three files: [StageZero/Program.cs](StageZero/Program.cs), [prod.docker-compose.yml](prod.docker-compose.yml), [docker-run.ps1](docker-run.ps1).
- **Phase A host setup (Windows rig):** ⏳ In progress. Owner is walking through [PHASE_A_DEPLOYMENT.md](PHASE_A_DEPLOYMENT.md).
- **Phase A verification (public URL live, reboot survival, etc.):** ⏳ Not started.
- **Phase B refactor:** ⏳ Not started. Do not begin until Phase A is fully verified.

---

## Phase A details

The host-side checklist lives in [PHASE_A_DEPLOYMENT.md](PHASE_A_DEPLOYMENT.md) — that is the document the operator follows on the Windows box. The summary here is for context.

### Code changes already merged on `claude/eager-gagarin-9eedc4`

1. **Forwarded-headers middleware** added to [StageZero/Program.cs](StageZero/Program.cs).
   `Microsoft.AspNetCore.HttpOverrides` imported; `ForwardedHeadersOptions` configured for `XForwardedFor | XForwardedProto`, with `KnownIPNetworks` and `KnownProxies` cleared (Cloudflare can come from anywhere); `app.UseForwardedHeaders()` called immediately after `var app = builder.Build()`. Without this, the app would see plain HTTP and generate insecure links / mis-detect the host.
2. **HTTPS redirect gated to Development** in [StageZero/Program.cs](StageZero/Program.cs). Cloudflare terminates TLS upstream, so the in-container app should not redirect to `https://localhost:5100` (which would 404 — no HTTPS listener).
3. **`prod.docker-compose.yml`** — new file. Plain HTTP on `5100:80`, `ASPNETCORE_ENVIRONMENT=Production`, `ASPNETCORE_URLS=http://+:80`, no PFX volume mount, `restart: unless-stopped`. [beta.docker-compose.yml](beta.docker-compose.yml) is left alone for dev workflows.
4. **`docker-run.ps1` retargeted** to `prod.docker-compose.yml`. Service name updated to `prod-stagezero`. (Pre-existing bug fixed: `logs`/`restart` subcommands previously referenced a non-existent service named `stagezero`.)

### Host setup (owner's Windows rig)

See [PHASE_A_DEPLOYMENT.md](PHASE_A_DEPLOYMENT.md). Briefly:

1. Install Docker Desktop + cloudflared.
2. `git clone` repo to `C:\StageZero`, checkout `claude/eager-gagarin-9eedc4`.
3. Create `.env` with SMTP placeholders.
4. `.\docker-run.ps1 up` → verify `http://localhost:5100`.
5. `cloudflared tunnel login` / `create stagezero` / `route dns stagezero stagezero.codelifter.net`.
6. Write `C:\Users\<user>\.cloudflared\config.yml` with the single ingress rule for `stagezero.codelifter.net → http://localhost:5100`.
7. `cloudflared --config <path> service install`.
8. Complete StageZero admin first-run **via the public URL**, not localhost.

### Phase A verification gates

Phase B does not start until all of these are green:

- `http://localhost:5100` from the Windows host returns the login page.
- `Get-Service cloudflared` shows Running, StartType Automatic.
- `https://stagezero.codelifter.net` loads from a phone on cellular (not home Wi-Fi) with a valid Cloudflare cert.
- DevTools → Network → WS shows `_blazor` SignalR connection 101 and stable.
- `.\docker-run.ps1 logs` shows request logs with `scheme=https` (proves forwarded-headers wiring).
- Container survives `down`/`up` cycle with admin account intact (proves `%APPDATA%\StageZero` volume mount).
- Public URL still works after a full Windows reboot with zero manual action.

---

## Phase B details

Cutover is in-place — the same tunnel ID created in Phase A is adopted by the new code, so the public CNAME doesn't change. Expected downtime during the `cloudflared service uninstall` → `service install <token>` swap: ~30 seconds.

### B1. Delete `StageZero.ReverseProxy` project

- Remove from [StageZero.sln](StageZero.sln) and from [StageZero/StageZero.csproj](StageZero/StageZero.csproj) project references.
- Delete the [StageZero.ReverseProxy/](StageZero.ReverseProxy) directory entirely.
- Remove the `using StageZero.ReverseProxy.Services;` import from [StageZero/Program.cs:14](StageZero/Program.cs:14) (line number from time of writing — re-grep before editing).
- Remove the three DI registrations in the block around [Program.cs:126-133](StageZero/Program.cs:126).
- Remove the inline `StubProxyConfigurationService` class at the bottom of `Program.cs` (was around lines 383-391).
- Net package effect: `Yarp.ReverseProxy 2.1.0` and `Certes 3.0.0` are gone from the solution.

### B2. Fold trimmed model + UI into StageZero

**Model** — new file `StageZero/Models/TunnelRoute.cs`:
- `int Id`
- `string DomainName` (unique index)
- `string ForwardScheme` (`http` | `https`)
- `string ForwardHost`
- `int ForwardPort`
- `bool IsEnabled`
- `string? Notes`
- `DateTime CreatedAt`, `DateTime UpdatedAt`

Drop everything else from the old `ProxyHost`: `UseLetsEncrypt`, `LetsEncryptEmail`, `SslCertificatePath/Key`, all HSTS, cert expiry tracking — Cloudflare handles every bit of this at the edge.

**DbContext** — in [StageZero/Data/ApplicationDbContext.cs](StageZero/Data/ApplicationDbContext.cs): replace the `DbSet<ProxyHost> ProxyHosts` declaration with `DbSet<TunnelRoute> TunnelRoutes`. Update the entity configuration block (was around lines 72-85) to match the trimmed shape.

**Schema migration** — the runtime DDL bootstrap in `Program.cs` (was around lines 286-346) does `CREATE TABLE IF NOT EXISTS ProxyHosts (...)`. Replace with a `DROP TABLE IF EXISTS ProxyHosts;` followed by `CREATE TABLE IF NOT EXISTS TunnelRoutes (...)` for the new shape. Phase A creates zero rows in `ProxyHosts` (no UI access during Phase A), so the drop is safe.

**Razor pages** — move and trim:

| From | To |
|---|---|
| `StageZero.ReverseProxy/BlazorUIPages/ProxyHosts.razor` | `StageZero/Application/Areas/TunnelManagement/TunnelRoutes.razor` (route `/tunnel-routes`) |
| `StageZero.ReverseProxy/BlazorUIPages/ProxyHostEdit.razor` | `StageZero/Application/Areas/TunnelManagement/TunnelRouteEdit.razor` |

Also: there are duplicate copies of these pages in [StageZero/Application/Areas/ProxyManagement/](StageZero/Application/Areas/ProxyManagement). Delete those too — net result is one canonical Tunnel Management area.

In the form: keep DomainName + Forward fields + IsEnabled + Notes. Remove all SSL/HSTS/cert sections.

### B3. New `CloudflareTunnelService`

Mirror the pattern in [StageZero/Services/Dns/CloudflareDnsService.cs](StageZero/Services/Dns/CloudflareDnsService.cs) — same `IHttpClientFactory`, same `Bearer` auth helper (the existing `CreateAuthenticatedClient(string apiToken)` is at the bottom of that file), same `https://api.cloudflare.com/client/v4` base URL.

New files:
- `StageZero/Services/Tunnel/ICloudflareTunnelService.cs`
- `StageZero/Services/Tunnel/CloudflareTunnelService.cs`

Interface surface:
- `Task<List<TunnelInfo>> ListTunnelsAsync(string apiToken, string accountId)` — `GET /accounts/{account_id}/cfd_tunnel`
- `Task<TunnelCreateResult> CreateTunnelAsync(string apiToken, string accountId, string name)` — `POST /accounts/{account_id}/cfd_tunnel` with `{ name, config_src: "cloudflare" }`. Returns tunnel ID + connector token.
- `Task<string> GetConnectorTokenAsync(string apiToken, string accountId, string tunnelId)` — `GET /accounts/{account_id}/cfd_tunnel/{tunnel_id}/token`
- `Task SyncIngressAsync(string apiToken, string accountId, string tunnelId, IEnumerable<TunnelRoute> enabledRoutes)` — `PUT /accounts/{account_id}/cfd_tunnel/{tunnel_id}/configurations`. Full ingress array, always ending in `{ service: "http_status:404" }` as catch-all.
- `Task EnsureCnameAsync(string apiToken, string zoneId, string hostname, string tunnelCname)` — `POST /zones/{zone_id}/dns_records` with type `CNAME`, content `<tunnel-id>.cfargotunnel.com`, proxied `true`. Reuse the lookup-then-update-or-create pattern from `CloudflareDnsService`.

Register in `Program.cs` alongside the existing `ICloudflareService` registration.

### B4. Tunnel settings + setup wizard

New page route `/settings/tunnel`. Stores config in a new `TunnelConfig` row (single-row table; or extend an existing settings table if one exists — verify when implementing):
- `CloudflareAccountId`
- `CloudflareZoneId`
- `CloudflareApiToken` (**encrypt** with ASP.NET Data Protection — `IDataProtector`. Do not store the raw token.)
- `TunnelId`
- `TunnelName`

Wizard flow:
1. User enters API token + Account ID.
2. Page lists zones the token can see; user picks `codelifter.net`.
3. Page calls `ListTunnelsAsync`. If a tunnel named `stagezero` exists (it will, from Phase A), offer "Adopt existing" — saves its ID. Otherwise offer "Create new."
4. On adopt or create, page calls `GetConnectorTokenAsync` and displays the token with instructions: "On the Windows host run `cloudflared service uninstall`, then `cloudflared service install <token>`. Reload this page when done."
5. After cutover, subsequent saves of tunnel routes automatically call `SyncIngressAsync` + `EnsureCnameAsync`.

**Required Cloudflare API token permissions (single token covers everything):**
- Account → Cloudflare Tunnel → Edit
- Zone → DNS → Edit (scoped to `codelifter.net`)
- Zone → Zone → Read

The same token can drive the existing DDNS feature too — single source of truth.

### B5. Cutover on the Windows host

Once B1–B4 are deployed and the setup wizard has been completed:

```powershell
cloudflared service uninstall
cloudflared service install <connector-token-from-StageZero-UI>
Remove-Item C:\Users\<user>\.cloudflared\config.yml
```

The tunnel ID is unchanged, so the existing `stagezero.codelifter.net` CNAME still resolves correctly. Verify `https://stagezero.codelifter.net` still works and the existing ingress rule appears in the StageZero UI's tunnel routes list. (The wizard should seed it from the prior `config.yml`; if not, add it manually — one row.)

### B6. Smoke test new subdomain

In the StageZero UI, add:
- DomainName: `test.codelifter.net`
- Forward: `http://localhost:5100` (point at StageZero itself for the test)

On save the UI calls `SyncIngressAsync` + `EnsureCnameAsync`. Within ~30s `https://test.codelifter.net` should resolve and load StageZero. Delete the test route afterward and confirm the CNAME + ingress rule both go away.

### Phase B verification gates

- `dotnet list package | grep -iE "yarp|certes"` returns nothing.
- `StageZero.ReverseProxy/` directory is gone; [StageZero.sln](StageZero.sln) no longer references it.
- Route `/proxy-hosts` returns 404; `/tunnel-routes` works.
- After cutover, `https://stagezero.codelifter.net` works exactly as in Phase A.
- Adding a tunnel route in the UI: CNAME appears in Cloudflare dashboard within seconds, hostname resolves and serves within ~30s.
- Disabling a route stops it serving but leaves the CNAME (for fast re-enable).
- Deleting a route removes both the CNAME and the ingress rule.
- Windows reboot: cloudflared service (now in token mode, no local config.yml) restarts and pulls config from Cloudflare with no manual action.

---

## File map

### Phase A — touched / created

| File | Action |
|---|---|
| [StageZero/Program.cs](StageZero/Program.cs) | Edited — forwarded headers + gated HTTPS redirect |
| [prod.docker-compose.yml](prod.docker-compose.yml) | Created — HTTP-only, port 5100:80, Production env |
| [docker-run.ps1](docker-run.ps1) | Edited — retargeted to prod compose, service name fix |
| [PHASE_A_DEPLOYMENT.md](PHASE_A_DEPLOYMENT.md) | Created — host-side checklist |
| [DEPLOYMENT_PLAN.md](DEPLOYMENT_PLAN.md) | Created — this document |

### Phase B — to touch / create

| File | Action |
|---|---|
| [StageZero.sln](StageZero.sln) | Edit — remove `StageZero.ReverseProxy` project |
| [StageZero/StageZero.csproj](StageZero/StageZero.csproj) | Edit — remove project reference |
| [StageZero.ReverseProxy/](StageZero.ReverseProxy) | Delete entire directory |
| [StageZero/Program.cs](StageZero/Program.cs) | Edit — drop ProxyHost wiring, replace DDL bootstrap, register `ICloudflareTunnelService` |
| `StageZero/Models/TunnelRoute.cs` | Create |
| [StageZero/Data/ApplicationDbContext.cs](StageZero/Data/ApplicationDbContext.cs) | Edit — `ProxyHost` → `TunnelRoute` |
| `StageZero/Services/Tunnel/ICloudflareTunnelService.cs` | Create |
| `StageZero/Services/Tunnel/CloudflareTunnelService.cs` | Create — reuse Bearer/HttpClientFactory pattern from `CloudflareDnsService` |
| `StageZero/Application/Areas/TunnelManagement/TunnelRoutes.razor` | Create (port + trim from `ProxyHosts.razor`) |
| `StageZero/Application/Areas/TunnelManagement/TunnelRouteEdit.razor` | Create (port + trim from `ProxyHostEdit.razor`) |
| `StageZero/Application/Areas/TunnelManagement/TunnelSettings.razor` | Create — setup wizard |
| [StageZero/Application/Areas/ProxyManagement/](StageZero/Application/Areas/ProxyManagement) | Delete duplicate razor pages |

### Reused, not rebuilt

- [docker-run.ps1](docker-run.ps1) — no changes after Phase A.
- [StageZero/Dockerfile](StageZero/Dockerfile) `release` stage — already runs `dotnet StageZero.dll` and EXPOSEs 80. Fits HTTP-only deployment as-is.
- [StageZero/Services/Dns/CloudflareDnsService.cs](StageZero/Services/Dns/CloudflareDnsService.cs) — DDNS stays. `CloudflareTunnelService` shares its `IHttpClientFactory` + Bearer auth helper.
- Form structure of the old `ProxyHosts.razor` / `ProxyHostEdit.razor` — port and trim, don't rewrite from scratch.

---

## Notes for the next Claude session

- The original conversation that produced this plan happened on macOS in a worktree (`.claude/worktrees/eager-gagarin-9eedc4`). The work now transitions to the Windows machine. The git branch carries everything — no local state is lost.
- `dotnet build -c Release` was clean on macOS at end of Phase A code. On Windows, expect the same — but on first build pull may warn about line endings (`.gitattributes` may need a touch if so).
- The owner has confirmed every decision in this plan. Don't re-ask "should we use Cloudflare Tunnel?" — that's settled. If something in Phase B looks wrong on closer inspection, propose the change with the trade-off, don't silently deviate.
- One known soft spot in Phase B: storing the Cloudflare API token. The plan says "encrypt with `IDataProtector`." Verify the data-protection keys are persisted to the mounted volume (`/app-data`) before relying on it across container restarts — otherwise the token decrypts to garbage after the next `down`/`up` cycle. ASP.NET's `AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo("/app-data/dp-keys"))` is the standard fix.
- Phase B's `SyncIngressAsync` is a **full replace** of the ingress array, not a delta. Always include every enabled route + the `http_status:404` catch-all, in priority order (most specific hostnames first).
- The Cloudflare Tunnel "remotely-managed" config requires `config_src: "cloudflare"` on tunnel create. Adopting a Phase-A tunnel created with the default `config_src: "local"` requires a `PATCH /accounts/{account_id}/cfd_tunnel/{tunnel_id}` to flip it — handle this in the adopt path of the setup wizard.
