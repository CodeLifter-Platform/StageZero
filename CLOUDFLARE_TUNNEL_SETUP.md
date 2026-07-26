# Cloudflare Tunnel Setup

StageZero publishes your services to the internet through a Cloudflare Tunnel. A
small daemon called `cloudflared` holds an outbound connection to Cloudflare, and
Cloudflare routes public requests back down it.

That means:

- **No port forwarding.** Nothing inbound is opened on your router.
- **No certificate management.** Cloudflare terminates TLS at its edge.
- **No public IP needed.** Works behind CGNAT.

StageZero manages the tunnel's *routes* through the Cloudflare API, so adding a
new subdomain is a form submission rather than a config file edit and a restart.

---

## Prerequisites

- A domain on Cloudflare (the zone must be active).
- StageZero running and reachable locally.
- Permission to install a service on the machine that will run `cloudflared`.

---

## 1. Create an API token

In the Cloudflare dashboard: **My Profile → API Tokens → Create Token → Custom token**.

| Scope | Permission |
|---|---|
| Account | Cloudflare Tunnel → **Edit** |
| Zone | DNS → **Edit** |
| Zone | Zone → **Read** |

Restrict the zone permissions to the domain you're publishing. The same token also
works for StageZero's DNS Configuration page, so one token covers both features.

You'll also need your **Account ID**, from **Workers & Pages → Account details** in
the dashboard sidebar.

---

## 2. Connect StageZero

Open **Tunnel Setup** (`/tunnel-settings`) and:

1. Paste the API token and Account ID, then **Connect**.
2. Pick the zone your hostnames belong to.
3. Either **Adopt** an existing tunnel or **Create** a new one.

The token is encrypted with ASP.NET Data Protection before it's stored. The
encryption keys live in the app data directory (`/app-data/dp-keys` in Docker), so
that directory must be on a persistent volume — otherwise the token becomes
unreadable after a restart and setup has to be repeated.

---

## 3. Install the connector

StageZero shows a **connector token** after step 2. Run the matching commands on
the machine that should serve the traffic. Treat the token like a password.

### macOS

```bash
brew install cloudflared
sudo cloudflared service install <token>
```

Verify: `sudo launchctl list | grep cloudflared`

### Windows

In an **Administrator** terminal:

```powershell
winget install --id Cloudflare.cloudflared
cloudflared service install <token>
```

Verify: `Get-Service cloudflared` (should be Running, StartType Automatic)

### Linux

```bash
curl -fsSL https://pkg.cloudflare.com/cloudflare-main.gpg \
  | sudo tee /usr/share/keyrings/cloudflare-main.gpg >/dev/null
echo "deb [signed-by=/usr/share/keyrings/cloudflare-main.gpg] https://pkg.cloudflare.com/cloudflared any main" \
  | sudo tee /etc/apt/sources.list.d/cloudflared.list
sudo apt update && sudo apt install cloudflared
sudo cloudflared service install <token>
```

Verify: `systemctl status cloudflared`

### Docker (alternative)

`prod.docker-compose.yml` ships a `cloudflared` sidecar behind a compose profile:

```bash
echo "TUNNEL_TOKEN=<token>" >> .env
docker compose -f prod.docker-compose.yml --profile tunnel up -d
```

Simpler to start, but the connector stops whenever the stack does, and routes can
only reach services on the compose network — point them at
`http://prod-stagezero:80` rather than `localhost`. A host service is the more
resilient choice.

---

## 4. Add a route

On **Tunnel Routes** (`/tunnel-routes`), click **Add Route**:

- **Hostname** — the public name, e.g. `app.yourdomain.com`. Must be in the zone
  chosen during setup.
- **Local Service** — where the connector forwards to, resolved from the machine
  running `cloudflared`. Private addresses are fine.

On save StageZero pushes the full ingress rule set to the tunnel and creates a
proxied CNAME pointing the hostname at `<tunnel-id>.cfargotunnel.com`. The
hostname usually starts serving within about 30 seconds.

Disabling a route removes its ingress rule but keeps the CNAME, so re-enabling
takes effect immediately. Deleting removes both.

---

## Running StageZero itself behind the tunnel

```bash
./docker-run.sh up prod        # macOS / Linux
.\docker-run.ps1 up prod       # Windows
```

This runs the release image on plain HTTP at `127.0.0.1:5100`. The app reads
`X-Forwarded-Proto` so it still generates `https://` links, and HTTPS redirection
is disabled outside Development — the container has no TLS listener to redirect to.

Then add a route pointing your hostname at `http://localhost:5100`.

Complete first-run admin setup **through the public URL**, not `localhost`, so the
session cookie is issued for the right host.

---

## Migrating from a local `config.yml`

If you already run a tunnel configured by a local `config.yml`:

1. In Tunnel Setup, **Adopt** that tunnel. StageZero switches it to
   remotely-managed configuration (`config_src: cloudflare`).
2. Re-create your existing ingress rules as tunnel routes in the UI.
3. On the host: `cloudflared service uninstall`, then
   `cloudflared service install <token>` with the token from the UI.
4. Delete the old `config.yml` so it can't take precedence.

The tunnel ID doesn't change, so existing CNAMEs keep resolving. Expect roughly 30
seconds of downtime during the service swap.

---

## Troubleshooting

**Hostname returns a Cloudflare 1033 error.** The connector isn't running or isn't
registered. Check the service status and `cloudflared` logs.

**Hostname returns 404 from the tunnel.** The request reached Cloudflare but no
ingress rule matched, so it hit the catch-all. Confirm the route exists and is
enabled, and that the hostname matches exactly.

**"Could not decrypt the stored Cloudflare API token."** The data-protection
keyring was lost — usually a container restart without a persistent `/app-data`
mount. Re-run Tunnel Setup, then fix the volume mount.

**Login redirects to localhost.** Forwarded headers aren't being honored. Confirm
you're running the `prod` compose target, which sets `ASPNETCORE_ENVIRONMENT=Production`.

**Route saved but nothing happens.** Check the app logs. A failed Cloudflare push
leaves the route saved locally so you can retry by re-saving it — the ingress sync
is a full replace, so one successful save repairs the whole rule set.
