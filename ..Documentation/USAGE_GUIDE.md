# Using StageZero

How an operator runs StageZero end to end: start it, create the admin account, then set up
dynamic DNS, a Cloudflare Tunnel and Access policies. For the Cloudflare side of every
step, especially which permissions the API token needs, keep
[CLOUDFLARE_TOKENS.md](CLOUDFLARE_TOKENS.md) open alongside this guide.

Building from source and running the test suite are covered in
[OnboardWeb.md](OnboardWeb.md) and [OnboardDocker.md](OnboardDocker.md).

---

## 1. Run it

StageZero is one container: the API serves the web app. The release compose file is the
instance you keep running.

```bash
cp .env.example .env
docker compose up -d --build
```

Open **http://localhost:5100**. Nothing in `.env` is required; the file documents every
option.

To run the published image instead of building one, set
`STAGEZERO_IMAGE=ghcr.io/codelifter-platform/stagezero:latest` (or `:pre`) in `.env`, then:

```bash
docker compose pull
docker compose up -d
```

What the compose file sets up:

- **Port.** The container listens on 8080 and is published on host port **5100**, on every
  interface by default so a phone or a `cloudflared` on another machine can reach it.
  `STAGEZERO_BIND=127.0.0.1` keeps it to this machine.
- **Data.** `/app-data` in the container is mounted from `STAGEZERO_DATA_DIR` (default
  `./.volumes/stagezero-data`). It holds the SQLite database, the logs and the Data
  Protection keys that decrypt your stored Cloudflare tokens. **It must persist and be
  backed up**: lose the keys and every stored token is unreadable.
- **Plain HTTP.** No certificate. In production StageZero sits behind a Cloudflare Tunnel,
  which terminates TLS; the app reads `X-Forwarded-Proto` so its links are still `https://`.
- **Health.** The image's `HEALTHCHECK` probes `/healthz`. `docker ps` shows it as healthy
  once the database is reachable.
- **Optional connector.** A `cloudflared` sidecar starts only with
  `docker compose --profile tunnel up -d` (see [section 6](#6-set-up-the-tunnel)).
- **Optional PostgreSQL.** SQLite is the default and needs no setup. To keep the data in a
  PostgreSQL server outside StageZero (the platform Infra stack), tick *Keep StageZero's data
  in PostgreSQL* on the first-run page, or use **Settings → Database** later: test, then
  **Copy and switch**. One way; [OnboardDocker.md](OnboardDocker.md#on-postgresql).

Useful commands:

```bash
docker compose logs -f stagezero
```

```bash
docker compose down
```

`./docker-run.sh up` (or `.\docker-run.ps1 up` on Windows) does the same as
`docker compose up` but puts the data in the platform's app-data directory.

---

## 2. First run: create the admin

A fresh install has no users, so every page redirects to **`/setup`**.

1. Enter the **Admin email** (your login) and a **Password** of at least 8 characters, twice.
2. **Register this install with codelifter.net** is ticked: StageZero sends the admin
   email and its version to codelifter.net, so CodeLifter knows who runs StageZero. It
   signs you up for nothing and sends you no email. Untick it to send nothing. The
   CodeLifter newsletter and StageZero update emails are separate, optional choices on
   **Settings → Email from CodeLifter**, each with its own confirmation email. Set
   `CodeLifter__SubscriptionsUrl=` (blank) in `.env` to switch all of it off.
3. Click **Create admin account**, then sign in.

There is no email verification here. StageZero never sends email.

If StageZero will sit behind a tunnel, you can do this step later **through the public
URL** rather than `localhost`, so the session cookie is issued for the right host.

That email also becomes the default **owner email** on every new tunnel route's Access
policy ([section 7](#7-publish-a-hostname)).

---

## 3. Sign in, sign out, and resetting a password

**Sign in** at `/login` with the admin email and password. The session is a cookie that
lasts 7 days (renewed while you use the app), survives a reload and a server restart, and
ends at the next request if the password or email changes. **Sign out** is at the top right.

Limits: 5 wrong passwords lock the account for 15 minutes, and one address gets 10 failed
sign-ins or reset requests per 15 minutes.

### Forgot password: the code is in the server log

StageZero does not send email, so a password-reset code goes to the **server log**.
Whoever can read the log controls the server, and that is who may reset the admin password.

1. On the sign-in page click **Forgot password?**, enter the admin email and click
   **Send reset code**. The page confirms the code was written to the log.
2. Read the code from the log. It is in a banner headed `STAGEZERO PASSWORD RESET CODE`:

   ```bash
   docker logs stagezero 2>&1 | grep -A6 "PASSWORD RESET CODE"
   ```

   Outside Docker, the logs are in `logs/` under the data directory
   (`~/.config/stagezero` on Linux, `~/Library/Application Support/StageZero` on macOS,
   `%APPDATA%\StageZero` on Windows).
3. Click **Enter the code** (or open `/reset-password`), enter the 6-digit code and a new
   password. The code expires after 15 minutes and is void after 5 wrong guesses; request
   another if it lapsed.
4. Sign in with the new password. StageZero then sends you to **Account setup** to verify
   your email: click **Send verification code**, read the code from the log (banner
   `STAGEZERO EMAIL VERIFICATION CODE`) and click **Verify**.

### When the log is out of reach

On the server itself, `reset-password` prints a one-time password and lifts any lockout:

```bash
docker exec -it stagezero dotnet StageZero.dll reset-password you@example.com
```

Sign in with it; **Account setup** then asks you to replace it with a new password.

The last resort, when even the admin email is forgotten, is in the README
(*Configuration → Last resort*). It deletes the user row so `/setup` comes back. DNS,
tunnel and Access settings are untouched.

---

## 4. Find your way around

Signed in, every page has:

- **The sidebar**: Home, IP Monitor, DNS Configuration, Tunnel Setup, Tunnel Routes.
  Its collapse button shrinks it to an icon rail; the choice is remembered in this browser.
- **The top bar**: the current public IP (it updates live when it changes), the theme
  toggle, the signed-in email (click it for **Settings**) and **Sign out**.

### Settings: the Cloudflare API token

StageZero reaches Cloudflare with **one** API token, for dynamic DNS, the tunnel and Access
alike. Click your email in the top bar to open **Settings**, where it lives:

1. The **Permissions the token needs** table lists the five rows to add when you create the
   token (Account → Cloudflare Tunnel, Access: Apps and Policies, Access: Service Tokens, all
   Edit; Zone → DNS → Edit; Zone → Zone → Read), with what each is for, and the steps for
   Cloudflare's *Create Token* page. Details: [CLOUDFLARE_TOKENS.md](CLOUDFLARE_TOKENS.md).
2. Paste the token and click **Save token**. Fill in **Account ID** only if the token
   reaches more than one account. StageZero verifies the token with Cloudflare first and
   refuses one that is disabled, expired or not valid yet (a custom start date in the
   future), then saves it encrypted and marks each permission *Granted* or *Missing*.
3. **Check permissions** re-runs the checks, **Replace token** swaps in a new value (that is
   how you [rotate it](CLOUDFLARE_TOKENS.md#rotating-the-token)), and **Remove** makes
   StageZero forget it. Removing doesn't revoke it: delete it in Cloudflare too.

The token is never shown again once saved. A token with only the two Zone rows runs dynamic
DNS; the tunnel and Access need the Account rows.

### Theme toggle

The button in the top bar reads **☀ Light** in the dark theme and **☾ Dark** in the light
theme. Clicking it switches the whole app at once. The choice is saved in this browser
(`localStorage`, key `stagezero.theme`) and applied before the page draws, so there is no
flash on reload. It is per browser, not per account. The sign-in and setup pages have the
same toggle.

---

## 5. Dynamic DNS

StageZero keeps Cloudflare **A** records pointed at your public IPv4 address.

### The IP monitor

A background check runs **every 3 minutes**. It asks three independent services
(Cloudflare, ipify and AWS) for your public IP at once. Two must agree before StageZero
believes the IP changed, and an answer that is not a public IPv4 address is discarded. An
inconclusive check records nothing and leaves DNS alone.

Every check, changed or not, also compares each auto-update record in Cloudflare against
the current IP and rewrites any that differ. A record someone edited by hand in the
dashboard is put back on the next check.

**IP Monitor** (`/ip-monitor`) shows:

- **Current public IP**, with when it was last confirmed. **Check now** runs a check
  immediately, including the DNS comparison.
- **IP address history**: one row per address, with when it was first seen, when it was
  last checked, and how many checks confirmed it. The table grows when the IP changes, not
  every 3 minutes.

### Add a DNS provider

A *provider* is one Cloudflare zone StageZero manages records in, reached with the token
saved on [Settings](#settings-the-cloudflare-api-token).

1. Save the token on Settings first. Until then, DNS Configuration points you there.
2. Open **DNS Configuration** (`/dns-config`). In the **Add a zone** card, click
   **Load zones**.
3. StageZero lists every zone the token can reach. Click **Add provider** next to the zone
   you want; the provider is named after the zone. Zones you already manage read *Added*.
4. A new provider starts with updates on. To stage records without touching DNS yet,
   switch it to **Updates disabled** on its card before adding records.

Each provider card has a switch, **Updates enabled / Updates disabled**. Switched off, IP
monitoring continues and the records stay listed, but StageZero makes no DNS changes for
that provider. Useful for testing, or for pausing updates during a migration.

### Add records

StageZero **does not create records** in Cloudflare for dynamic DNS. It tracks records that
already exist there and keeps their address current. Create the A record in Cloudflare
first (any address will do; StageZero corrects it on its next check), then track it here:

- **Import all records** pulls every A, AAAA and CNAME record in the zone into StageZero.
  A records are set to auto-update; records already tracked are skipped.
- **Add DNS record** tracks one. Click **Load existing records from Cloudflare** to pick
  one from the zone, or enter the **Record name** (for example `home.example.com`) and
  **Record type** by hand. Tick **Auto-update when the IP changes**.

Only **A** records can auto-update, because the IP monitor finds your IPv4 address. AAAA
and CNAME records are tracked and listed but never changed; the checkbox is disabled for
them, with the reason.

An update changes only the record's address. Its proxy setting (orange cloud), TTL,
comment and tags are left as they were.

The records table shows each record's type, current content or last IP, when StageZero
last updated it, and whether auto-update is on. CNAMEs that point at another tracked
record are nested under it; **Show all CNAMEs** lists the rest. A CNAME row has a
**Tunnel route** button that opens its tunnel route, or starts one for that hostname.

**Edit** changes a tracked record (including its Cloudflare record ID, which StageZero
otherwise looks up by name). **Delete** and **Delete provider** remove records from
StageZero only. Cloudflare keeps them, and nothing in DNS changes.

---

## 6. Set up the tunnel

A Cloudflare Tunnel publishes services on your network at your own hostnames, with no
port forwarding, no certificates and no public IP needed. The detailed runbook, including
migrating from a local `config.yml`, is [CLOUDFLARE_TUNNEL_SETUP.md](CLOUDFLARE_TUNNEL_SETUP.md).

1. Save a token with the Account permissions on
   [Settings](#settings-the-cloudflare-api-token). Settings shows whether it has them.
2. Open **Tunnel Setup** (`/tunnel-settings`). **1. Cloudflare account** shows the account
   the token belongs to; click **Connect**. StageZero lists the zones and tunnels it can see.
3. Under **2. Zone and tunnel**, pick the **Zone** your hostnames belong to. Then either:
   - **Adopt** a tunnel already on the account. A tunnel marked *Local file* is switched to
     remotely-managed configuration. Adopting a tunnel that serves traffic interrupts it:
     StageZero becomes the source of truth for its routes, so re-create them here.
   - Or name a new one and click **Create tunnel**.
4. **3. Install the connector** shows the **connector token** and the install commands for
   **macOS**, **Windows**, **Linux** and **Docker**, with the token filled in. Run them on
   the machine that should serve the traffic. Treat the token like a password: anyone
   holding it can serve traffic on your tunnel.

For the Docker option, the token goes in `.env` and the sidecar starts with the stack:

```bash
echo "TUNNEL_TOKEN=<connector token>" >> .env
docker compose --profile tunnel up -d
```

The sidecar stops whenever the stack does, and can only reach services on the compose
network (point routes at `http://stagezero:8080`, not `localhost`). A host service is the
more resilient choice.

Afterwards, **Current configuration** at the top of Tunnel Setup shows the tunnel, its ID,
the zone and the Account ID. **Show connector token** fetches the token again from
Cloudflare when you need to reinstall the connector. **Disconnect** forgets the saved
tunnel; the API token on Settings stays. The tunnel and its routes stay on Cloudflare, but
StageZero stops managing them until you connect again.

---

## 7. Publish a hostname

**Tunnel Routes** (`/tunnel-routes`) lists every published hostname, where it forwards,
its Access mode (a red **Public** badge means no policy at all) and whether it is
**Active**. A banner says which tunnel and zone StageZero is connected to, or warns that
setup is not finished; routes cannot reach Cloudflare until it is.

Click **Add route**. The form has four sections.

**Public hostname.** For example `app.example.com`. It must be in the zone chosen in Tunnel
Setup. StageZero creates a proxied CNAME for it automatically. **Notes** are for you.

**Local service.** Where `cloudflared` forwards requests: **Scheme** (HTTP or HTTPS),
**Forward host** (an IP, a hostname or a Docker container name) and **Forward port**. It is
resolved from the machine running `cloudflared`, so a private address is fine. To publish
StageZero itself with a host-installed connector, use `http`, `localhost`, `5100`.

**Cloudflare Access.** Who may reach the hostname. **Protection** is one of:

| Mode | Who gets in |
|---|---|
| **Identity: people sign in** | People who sign in through an identity provider and match an allowed email or email domain |
| **Service token: machines only** | Callers presenting a Cloudflare service token |
| **Both: people and machines** | Either |
| **None: public, no policy** | Everyone. StageZero warns, and removes any Access application it created for the hostname |

New routes start on **Identity**, with **Allowed emails** pre-filled with the owner email
(the admin from setup, unless the `Access.OwnerEmail` setting says otherwise) and a
**Session duration** of `24h`. Add more emails, one per line, or **Allowed email domains**
to let in everyone at a domain. If the token can read identity providers, you can tick the
ones to offer; leave them all unticked to offer every provider on the account (a fresh
Zero Trust account has one-time PIN by email).

For a service token, either keep **Create a new token** on and give it a **Token name**
and optional **Token lifetime** (Cloudflare defaults to one year), or switch it off and
choose an **Existing token**.

**Status.** **Enabled** publishes the route. Disabling removes its ingress rule but keeps
its DNS record, so re-enabling takes effect immediately.

Click **Create route**. StageZero pushes the ingress rules, writes the CNAME, then creates
the Access application and policies. The hostname usually serves within about 30 seconds.

### The service token secret is shown once

If the save minted a service token, a **Service token created** dialog appears with the
**CF-Access-Client-Id** and **CF-Access-Client-Secret**. Cloudflare returns the secret only
at creation and StageZero stores it nowhere, so copy both into your vault now. The dialog
will not close on an outside click; tick **I have saved the client secret**, then **Done**.
Clients send both values as headers:

```bash
curl https://app.example.com/ \
  -H "CF-Access-Client-Id: <client id>" \
  -H "CF-Access-Client-Secret: <client secret>"
```

A lost secret cannot be recovered. Rotate the token in the Cloudflare Zero Trust dashboard
and update every client.

### When a save fails

If Access setup fails (most often a token missing an Access permission), StageZero **rolls
the route back**: it disables the route and removes its DNS record, so the hostname is not
live without a policy. The error names what failed. Fix the token
([errors table](CLOUDFLARE_TOKENS.md#errors-when-a-permission-is-missing)), then edit the
route, switch **Enabled** on and save again. Any other failed push leaves the route saved
in StageZero so you can retry by saving it again; the ingress sync replaces the whole rule
set, so one good save repairs everything.

### Manage routes

Each row has:

- **Access**: the route's **Access Status** page. It reads Cloudflare directly: the
  application, its session duration and identity providers, every attached policy with its
  rules, and the service token with its expiry and how many routes use it. It warns when
  reality and the configured mode disagree, for example a hostname with no application in
  front of it.
- **Edit**: the same form. Saving again is safe; StageZero updates what exists rather than
  creating duplicates, and recreates anything deleted in the dashboard.
- **Enable / Disable**: publishes or unpublishes the hostname without deleting anything.
- **Delete**: removes the ingress rule, the DNS record, the Access application and its
  policies. A service token is deleted only if StageZero created it and no other route uses
  it; otherwise StageZero leaves it and says why.

Access in depth (policies, idempotency, teardown, the `IAccessSecretSink` vault hook):
[CLOUDFLARE_ACCESS_SETUP.md](CLOUDFLARE_ACCESS_SETUP.md).

---

## 8. Day to day

- **Logs**: `docker compose logs -f stagezero`, and `logs/` in the data directory (one file
  a day, 31 kept). `STAGEZERO_LOG_LEVEL=Debug` in `.env` for more detail.
- **Health**: `/healthz` answers without signing in. It reports *Degraded* (still HTTP 200)
  when the public IP has not been confirmed for 15 minutes, which usually means the IP
  services are unreachable from your network.
- **Backups**: the data directory. With PostgreSQL, back up the database *and* the data
  directory; the directory still holds the keys that decrypt the Cloudflare tokens.
- **Upgrades**: `docker compose pull && docker compose up -d` with a published image, or
  `docker compose up -d --build` from an updated checkout. Migrations run at startup.
