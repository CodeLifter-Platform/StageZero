# Phase A — Deploy StageZero to Windows via Cloudflare Tunnel

This is the host-side checklist for the first deployment of StageZero behind a Cloudflare Tunnel on the Windows machine.

**Goal:** `https://stagezero.codelifter.net` serves the app from a Docker container on this Windows host, with TLS terminated by Cloudflare and no router port forwarding required.

**What's already done in code** (on branch `claude/eager-gagarin-9eedc4`):
- `StageZero/Program.cs` — forwarded-headers middleware added for Cloudflare; `UseHttpsRedirection` gated to Development only.
- `prod.docker-compose.yml` — new file. Plain HTTP on port 5100, `ASPNETCORE_ENVIRONMENT=Production`, `restart: unless-stopped`. No PFX / dev cert needed.
- `docker-run.ps1` — retargeted to the new compose file and the `prod-stagezero` service name.

Phase B (refactor — rip YARP/Certes, build API-managed tunnel UI inside StageZero) follows this; do not start it until Phase A is verified green.

---

## 1. Prerequisites

- **Docker Desktop for Windows** installed with the WSL2 backend.
  Verify in PowerShell:
  ```powershell
  docker version
  ```
- **cloudflared for Windows** — download the latest `.msi` from <https://github.com/cloudflare/cloudflared/releases> and install.
  Verify:
  ```powershell
  cloudflared --version
  ```

## 2. Get the code on the host

Use a stable path — cloudflared and Docker Desktop both reference these locations across reboots.

```powershell
cd C:\
git clone <your-repo-url> StageZero
cd C:\StageZero
git checkout claude/eager-gagarin-9eedc4
```

## 3. Create `.env` at `C:\StageZero\.env`

```env
Email__SmtpHost=
Email__SmtpPort=587
Email__SmtpUsername=
Email__SmtpPassword=
Email__FromEmail=
Email__FromName=StageZero
```

Leave SMTP blank for now — it's optional for first boot. The Cloudflare API token is configured later via the StageZero UI, not here.

## 4. Build and run the container

```powershell
cd C:\StageZero
.\docker-run.ps1 up
```

Verify locally first:

- Open <http://localhost:5100> on the Windows host. The StageZero login page should render.
- **Do not create the admin account yet.** Wait until the tunnel is up so the account is created via the public URL.

Useful follow-ups:
- `.\docker-run.ps1 logs` — tail container logs
- `.\docker-run.ps1 down` — stop
- `.\docker-run.ps1 restart` — restart the running service

## 5. Cloudflare Tunnel — create

Run in an **elevated** PowerShell.

```powershell
cloudflared tunnel login
```
This opens a browser. Pick `codelifter.net`. A cert is dropped at `C:\Users\<you>\.cloudflared\cert.pem`.

```powershell
cloudflared tunnel create stagezero
```
This prints a **tunnel UUID** and writes credentials to `C:\Users\<you>\.cloudflared\<UUID>.json`. Save the UUID — you need it in the next step.

```powershell
cloudflared tunnel route dns stagezero stagezero.codelifter.net
```
This creates the `stagezero.codelifter.net` CNAME pointing to `<UUID>.cfargotunnel.com` in your Cloudflare zone.

## 6. Create `C:\Users\<you>\.cloudflared\config.yml`

Replace `<UUID>` and `<you>` with real values.

```yaml
tunnel: <UUID>
credentials-file: C:\Users\<you>\.cloudflared\<UUID>.json
ingress:
  - hostname: stagezero.codelifter.net
    service: http://localhost:5100
  - service: http_status:404
```

This `config.yml` is intentionally throwaway — Phase B moves all this configuration inside the StageZero app itself.

## 7. Install cloudflared as a Windows service

```powershell
cloudflared --config C:\Users\<you>\.cloudflared\config.yml service install
Get-Service cloudflared
```

Expected: `Status: Running`, `StartType: Automatic`.

Useful follow-ups:
- `cloudflared tunnel info stagezero` — show active connectors
- `Restart-Service cloudflared` — pick up config changes
- `cloudflared service uninstall` — remove (used in Phase B cutover)

## 8. First boot of StageZero

1. Open <https://stagezero.codelifter.net> in any browser.
2. Complete StageZero's first-run admin setup at the public URL. The admin account is now bound to the public domain.

### Optional — DDNS for the rest of the zone

(You can skip this until Phase B if you prefer.)

1. In the Cloudflare dashboard → My Profile → API Tokens, create a token with:
   - Permission: **Zone → DNS → Edit**
   - Permission: **Zone → Zone → Read**
   - Zone Resources: `codelifter.net` only
2. In the StageZero UI, add the Cloudflare DNS provider with that token.
3. Configure it to track `@` (apex) and/or `www` — whatever you want to keep pointed at your current public IP. **Do not** point DDNS at the `stagezero` record; that's the tunnel CNAME and must not be overwritten.

## 9. Verification

| Check | How |
|---|---|
| Container reachable locally | <http://localhost:5100> from the Windows host shows the login page |
| Tunnel service healthy | `Get-Service cloudflared` shows Running; `cloudflared tunnel info stagezero` lists ≥1 active connector |
| Public reach works | `https://stagezero.codelifter.net` loads from a **phone on cellular** (not your home Wi-Fi) with a valid Cloudflare cert |
| WebSockets / Blazor SignalR work | DevTools → Network → WS — `_blazor` connection is Status 101 and stays open while you click around |
| Forwarded headers wired correctly | `.\docker-run.ps1 logs` shows request logs with `scheme=https` and the public hostname, not `http`/`localhost` |
| State persists across container restarts | `.\docker-run.ps1 down`, then `.\docker-run.ps1 up`. Log back in — admin account and any saved config are still there |
| Survives a host reboot | Restart Windows. After login, Docker Desktop auto-starts, the container comes up, cloudflared reconnects, public URL works with no manual intervention |

## Troubleshooting

- **`https://stagezero.codelifter.net` returns Cloudflare error 1033 ("tunnel not found")** — cloudflared service isn't running. Check `Get-Service cloudflared` and `cloudflared tunnel info stagezero`. Restart-Service cloudflared.
- **Public URL returns 502** — tunnel is up but can't reach `http://localhost:5100`. Confirm the container is running (`docker ps`) and that `localhost:5100` works from PowerShell (`curl http://localhost:5100`).
- **Localhost smoke test redirects to https** — make sure you pulled the latest branch; `UseHttpsRedirection` should only run in Development now.
- **Blazor UI loads but interactions hang** — WebSocket upgrade isn't getting through. Confirm `_blazor` WS in DevTools and check `cloudflared tunnel info stagezero` shows the connector is healthy. Re-`.\docker-run.ps1 restart` if needed.
- **Container won't start: cert / Kestrel errors** — you may be running the wrong compose file. `docker-run.ps1` must reference `prod.docker-compose.yml`, not `beta.docker-compose.yml`.
