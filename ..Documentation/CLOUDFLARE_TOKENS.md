# Cloudflare tokens

Every Cloudflare credential StageZero uses, what each one is for, and how to create, enter,
rotate and debug it. This is the canonical permission list; the tunnel and Access guides
link here rather than repeat it.

- [CLOUDFLARE_TUNNEL_SETUP.md](CLOUDFLARE_TUNNEL_SETUP.md): connecting a tunnel and
  installing `cloudflared`.
- [CLOUDFLARE_ACCESS_SETUP.md](CLOUDFLARE_ACCESS_SETUP.md): Access modes, policies and
  service tokens in depth.
- [USAGE_GUIDE.md](USAGE_GUIDE.md): the app end to end.

No real token appears anywhere in this repo. Values below like `<api token>` are
placeholders.

---

## The four credentials

| Credential | Secret? | Where it comes from | Where it goes in StageZero | Stored by StageZero |
|---|---|---|---|---|
| **API token** | Yes | You create it in the Cloudflare dashboard | **Settings** (click your email, top right) → **Cloudflare API token**. One token for DNS, Tunnel and Access | Yes, encrypted with ASP.NET Data Protection |
| **Account ID** | No | Worked out from the token; the dashboard URL if the token reaches several accounts | **Settings → Account ID (optional)** | Yes, plain |
| **Connector token** | Yes | StageZero fetches it from Cloudflare after tunnel setup | Not entered. You pass it to `cloudflared service install` or `TUNNEL_TOKEN` | No. Fetched from Cloudflare each time it is shown |
| **Access service token** (client ID + client secret) | The secret is | StageZero mints it when a route uses `service_token` or `both` | Not entered. Your clients send it as headers | Client ID only. **The secret never** |

The API token is the only one you create by hand. The rest of this guide is mostly about
getting its permissions right.

---

## API token permissions

StageZero calls the Cloudflare v4 API with the token as a bearer token. The permissions
below come from the endpoints it calls (`StageZero/Services/Dns/CloudflareDnsService.cs`,
`Services/Tunnel/CloudflareTunnelService.cs`, `Services/Access/CloudflareAccessService.cs`).

### Zone versus account scope

Cloudflare permissions come in two scopes, and StageZero needs both:

- **Zone** permissions (DNS, Zone) apply to the domains you pick under *Zone Resources*.
  Dynamic DNS and the tunnel's CNAMEs are zone work.
- **Account** permissions (Cloudflare Tunnel, Access) apply to the account you pick under
  *Account Resources*. Tunnels and Access are account-level objects. An Access application
  protects a hostname, but it lives on the account, not the zone.

A token can write DNS records perfectly well and still have no Access rights at all. That
is the most common setup mistake.

### By feature

| Feature | Scope | Permission | What StageZero does with it |
|---|---|---|---|
| **Dynamic DNS** | Zone | Zone → **Read** | Lists zones on the DNS Configuration page's *Add a zone* card (`GET /zones`) |
| | Zone | DNS → **Edit** | Lists records (import, "Load existing records") and rewrites an A record's address (`PATCH /zones/{zone}/dns_records/{id}`) |
| **Cloudflare Tunnel** | Zone | Zone → **Read** | Lists zones during Tunnel Setup |
| | Account | Cloudflare Tunnel → **Edit** | Lists, creates and adopts tunnels, fetches the connector token, pushes ingress rules (`/accounts/{account}/cfd_tunnel/...`) |
| | Zone | DNS → **Edit** | Creates, updates and deletes each route's proxied CNAME to `<tunnel-id>.cfargotunnel.com` |
| **Cloudflare Access** (on by default for new routes) | Account | Access: Apps and Policies → **Edit** | Creates, updates and deletes the Access application (`/access/apps`) and the account-level reusable policies (`/access/policies`) |
| | Account | Access: Service Tokens → **Edit** | Lists, mints and deletes service tokens (`/access/service_tokens`). Needed for the `service_token` and `both` modes |
| | Account | Access: Organizations, Identity Providers, and Groups → **Read** | *Optional.* Fills the identity-provider picker in the route editor (`/access/identity_providers`) |

**A note on the Access permission name.** The rest of this repo, and the error StageZero
shows, call the first Access permission "Access: Apps Edit". Cloudflare's permission list
has both *Access: Apps* (applications only) and *Access: Apps and Policies* (applications
and policies), plus a separate *Access: Policies*. StageZero writes **account-level reusable
policies** as well as applications, so grant **Access: Apps and Policies → Edit**, which
covers both. A token with *Access: Apps → Edit* but no policy permission passes StageZero's
pre-check (which only probes applications and service tokens) and is then likely to fail
when the policy is written. That case has not been tested against a live account; the
route rolls back either way (see [Errors](#errors-when-a-permission-is-missing)).

Edit, not Read, throughout. StageZero's Access pre-check can only prove the token can
*read* the endpoints. A Read-only token passes the check and then fails on the first write
with Cloudflare's own error.

### One token

StageZero keeps exactly one token, on the **Settings** page, and every feature uses it. The
page shows the five rows it needs and, after a save or **Check permissions**, which of them
this token has:

| Scope | Item | Level | Without it |
|---|---|---|---|
| Account | Cloudflare Tunnel | Edit | No tunnel setup or route changes |
| Account | Access: Apps and Policies | Edit | Routes in `identity` or `both` mode fail and roll back |
| Account | Access: Service Tokens | Edit | Routes in `service_token` or `both` mode fail and roll back |
| Zone | DNS | Edit | No DNS updates, no tunnel CNAMEs |
| Zone | Zone | Read | Nothing can find a zone |

*Access: Organizations, Identity Providers, and Groups → Read* is optional on top: it fills
the identity-provider picker in the route editor.

**Least privilege** still works within one token. A token with only the two Zone rows,
limited to the zones you update, runs dynamic DNS; Settings saves it, reports the Account
rows as missing, and Tunnel Setup asks for a token with account permissions. If you only
want dynamic DNS and tunnels, leave the Access permissions off and set each route's Access
mode to `none`. Routes default to `identity`, though, and saving one without Access
permissions fails and rolls back.

Installs from before the Settings page kept a token per DNS provider and one for the tunnel.
The upgrade keeps one of them: the tunnel's if there was a tunnel (it carried the most
permissions), else the first enabled provider's. If your providers used tokens for zones the
kept token can't see, **Check permissions** shows it; replace the token with one that covers
every zone.

---

## Create the token in the Cloudflare dashboard

1. Sign in at `dash.cloudflare.com`, open the profile menu (top right) and choose
   **My Profile → API Tokens**.
2. Click **Create Token**, then **Create Custom Token → Get started**. (A template such as
   *Edit zone DNS* covers dynamic DNS alone, but not tunnels or Access.)
3. Give it a name you will recognise, for example `StageZero`.
4. Under **Permissions**, add one row per permission. Each row is three dropdowns:
   scope, permission and level.

   | Scope | Permission | Level |
   |---|---|---|
   | Zone | Zone | Read |
   | Zone | DNS | Edit |
   | Account | Cloudflare Tunnel | Edit |
   | Account | Access: Apps and Policies | Edit |
   | Account | Access: Service Tokens | Edit |
   | Account | Access: Organizations, Identity Providers, and Groups | Read |

   For a DNS-only token, stop after the first two rows.
5. Under **Account Resources**, choose **Include → your account**. (This only appears once
   an Account permission is added.)
6. Under **Zone Resources**, choose **Include → Specific zone → your domain**, or
   *All zones from an account* if you will publish across several. Restrict it to the zones
   you actually use.
7. Optionally set **Client IP Address Filtering** to the public IP StageZero runs from, and
   a **TTL**. If you set an IP filter, remember that StageZero exists because that IP
   changes; a filtered token stops working the moment it does.
8. **Continue to summary → Create Token.**
9. Copy the token. **Cloudflare shows it once.** Store it in your password manager straight
   away (for a CodeLifter install, a 1Password item named `StageZero — CLOUDFLARE_API_TOKEN`).

Cloudflare's summary page offers a `curl` to verify the token. Run it before pasting the
token into StageZero; it proves the token is active, though not which permissions it has.

### The Account ID

Not a secret, and usually not needed: when you save the token, StageZero asks Cloudflare which
accounts it reaches and uses the only one. Enter it only if the token reaches several. It is
the hex string in the dashboard URL (`dash.cloudflare.com/<account-id>/…`), and is in the
**API** section on the right of any zone's **Overview** page.

Don't give the token a custom start date in the future (Cloudflare's *Custom* expiry sets a
window). Cloudflare accepts it but refuses every call until then; StageZero's Settings page
refuses to save such a token and says when it starts working.

---

## Where the token goes in StageZero

**Settings → Cloudflare API token.** Open Settings by clicking your email at the top right.
Paste the token (and the Account ID, only if it reaches several accounts) and click **Save
token**. StageZero verifies it with Cloudflare first, refusing one that is disabled, expired
or not valid yet, then saves it and runs one read-only probe per permission. The table shows
*Granted* or *Missing* for each row. Cloudflare can't report an Edit right without a write,
so DNS → Edit reads *Granted* when records can be read; the first update proves the rest.

**Dynamic DNS.** **DNS Configuration → Add a zone → Load zones**. StageZero lists every
zone the saved token can reach; click **Add provider** next to each one you want (zones
already added read *Added*). The provider is named after its zone.

**Tunnel and Access.** **Tunnel Setup → 1. Cloudflare account** shows the account from
Settings; click **Connect** to list its zones and tunnels, then create or adopt one.

The token is encrypted with ASP.NET Data Protection before it reaches the database, and it
is never sent back to the browser. The keys that decrypt it live in the data directory
(`dp-keys/`, `/app-data/dp-keys` in Docker). Lose that directory and the stored token is
unreadable; Settings says so, and DNS updates, tunnel changes and Access stop until you
enter the token again.

---

## Rotating the token

Most permission problems need no rotation at all. **Editing a token's permissions in
Cloudflare keeps the same token value**, so after *My Profile → API Tokens → … → Edit*,
nothing changes in StageZero. Just retry what failed.

When the token itself must change (it leaked, it expired, or you are rolling it on a
schedule):

1. **Create the new token** in Cloudflare with the same permissions, or use **Roll** on the
   existing token's menu, which issues a new value for the same token. Either way, copy the
   new value now.
2. **Settings → Replace token.** Paste the new value and click **Replace token**. It is
   verified and checked like a new one, and every feature uses it from the next call. The
   tunnel ID, the CNAMEs and the connector token don't change, so `cloudflared` keeps running
   and nothing goes offline. While a tunnel is saved, a token for a *different* account is
   refused: disconnect the tunnel on Tunnel Setup first if you really are moving accounts.
3. **Revoke the old token** in Cloudflare (*Delete* on the old token, unless you used
   *Roll*, which already invalidated it). **Remove** on the Settings page only makes
   StageZero forget the token; it keeps working at Cloudflare until you delete it there.
4. **Check.** On **IP Monitor**, click **Check now**; the server log should show no
   "Failed to update Cloudflare DNS record" lines. Open a route's **Access** page; it reads
   Cloudflare live with the new token.

The **connector token** is a separate credential and is not affected by any of this.
StageZero has no way to rotate it; it only reads it from Cloudflare.

---

## Access service tokens and the one-time secret

A route in `service_token` or `both` mode is guarded by a Cloudflare Access service token:
a client ID and a client secret that callers send as `CF-Access-Client-Id` and
`CF-Access-Client-Secret` headers.

**Cloudflare returns the client secret exactly once, when the token is created.** No
endpoint returns it again. So when StageZero mints one (saving a route with *Create a new
token* switched on, or re-enabling a route whose token was still pending), it shows the
secret in a dialog that:

- cannot be closed by clicking outside it,
- says it will not be shown again,
- only closes after you tick **I have saved the client secret**.

StageZero never logs the secret, never writes it to the database and never writes it to
disk. It keeps the client ID, the token name and its expiry. Copy the secret into your
vault before ticking the box.

If the secret is lost, there is no recovery. Rotate the token in the Cloudflare Zero Trust
dashboard (*Access → Service Auth*) and update every client. To send each new secret
straight to a vault as it is minted, implement `IAccessSecretSink`
([CLOUDFLARE_ACCESS_SETUP.md](CLOUDFLARE_ACCESS_SETUP.md#sending-the-secret-somewhere-durable)).

Service tokens expire (Cloudflare's default is one year; set *Token lifetime* in the route
editor to change it). The route's **Access** page shows the expiry.

---

## Errors when a permission is missing

These are the messages StageZero shows, with what is usually missing. Text in `‹…›` is
Cloudflare's own error detail, passed through. Where a status code appears (on screen or
in the server log), it tells you which kind of problem it is: **Unauthorized** means the
token is wrong, revoked or expired. **Forbidden** means the token is valid but lacks a
permission or does not cover that zone or account.

### Settings

| Message | Cause |
|---|---|
| `Cloudflare did not accept this token: Cloudflare could not verify the token: Invalid API Token` | A typo, a revoked token, or an account-owned token whose account isn't given (enter the Account ID) |
| `This token can't be used until <date> UTC. …` | The token has a custom start date in the future. Edit it in Cloudflare and choose a preset expiry |
| `This token has expired. …` / `Cloudflare reports this token as disabled. …` | Roll it, re-enable it, or create a new one |
| `This token can see several accounts (…). Enter the Account ID StageZero should use.` | Fill in *Account ID* |
| `This token can't see account <id>. …` | The Account ID is wrong, or not in the token's *Account Resources* |
| `The tunnel <name> is managed in account <id>, but this token is for account <id>. …` | Replacing with another account's token while a tunnel is saved. Disconnect the tunnel first, or use a token for the same account |
| `No Cloudflare API token is saved. Add one on the Settings page …` (on any page) | No token yet, or it was removed |
| `The saved Cloudflare API token can't be decrypted …` | The `dp-keys` directory was lost. Enter the token again |

### DNS Configuration

These messages do not carry Cloudflare's status code. The server log does: look for
`Error getting Cloudflare zones` or `Error getting Cloudflare DNS records`, followed by
`Cloudflare list failed: <status>`.

| Message | Missing |
|---|---|
| `Failed to load zones: Could not get zones` (Load zones on DNS Configuration) | The token is wrong, revoked or expired (Unauthorized in the log), or lacks Zone → Zone → Read (Forbidden) |
| `No zones found for this API token` | The token works but no zone is in its *Zone Resources* |
| `Failed to load DNS records: Could not get DNS records` (Load existing records) | Zone → DNS (Read at least; grant Edit), or the token was revoked |
| `Error importing records: Could not get DNS records` (Import all records) | Same |

Auto-updates run in the background, so their failures go to the **server log**, not the
screen:

- `Failed to update Cloudflare DNS record` followed by `Failed to verify DNS record <name>`:
  the token can read records but not write them. Grant DNS → Edit.
- `Could not list DNS records for provider <name>`: the token cannot read the zone's
  records at all, or has been revoked.
- `Skipping DNS verification: no usable Cloudflare API token is saved`: add one on Settings.

### Tunnel Setup

| Message | Missing |
|---|---|
| `The saved Cloudflare API token has no account permissions, which the tunnel and Access need. …` | Every Account row: the token is zone-only. Replace it on Settings |
| `Could not reach Cloudflare with the saved token: Could not get zones` | The token is wrong, revoked or expired, or lacks Zone → Zone → Read (the log has the status) |
| `Could not reach Cloudflare with the saved token: Cloudflare API error while trying to list tunnels: Forbidden — ‹…›` | Account → Cloudflare Tunnel → Edit, or the account is not in the token's *Account Resources* |
| `The token is valid but can't see any zones. Check that it has Zone → Zone → Read.` | Zone → Zone → Read, or no zone in *Zone Resources* |
| `That zone isn't visible to this token.` | The chosen zone is not in the token's *Zone Resources* |
| `Could not create the tunnel: …` / `Could not adopt the tunnel: …` | Cloudflare Tunnel → **Edit** (Read is not enough) |
| `Could not fetch the connector token: …` | Cloudflare Tunnel → Edit, or the tunnel was deleted in Cloudflare |

### Tunnel Routes

| Message | Missing |
|---|---|
| `Could not save the route: Cloudflare API error while trying to write CNAME for <host>: Forbidden — ‹…›` | Zone → DNS → Edit on the tunnel's zone |
| `Could not save the route: Cloudflare API error while trying to sync ingress rules: …` | Cloudflare Tunnel → Edit |
| `Could not save the route: Cloudflare Access setup failed for <host>: The saved Cloudflare API token cannot manage Access. Add these account-scoped permissions and save the token again on the Settings page: Access: Apps Read and Access: Apps Edit; Access: Service Tokens Read and Access: Service Tokens Edit. The route has been disabled and its DNS record removed, …` | The Access permissions named. The service-token one is listed whenever it is missing but only blocks `service_token` and `both` routes |
| `Could not save the route: Cloudflare Access setup failed for <host>: ‹…› The route has been disabled and its DNS record removed, …` (any other Cloudflare detail) | Usually an Edit level that is only Read, or the policy permission (see the note above) |
| `Cloudflare Tunnel is not configured. Save a Cloudflare API token on the Settings page, then complete Tunnel Setup.` | Not a permission problem. Tunnel Setup was never finished, no token (or a zone-only one) is saved, or the token can no longer be decrypted (lost `dp-keys`) |

When Access fails, StageZero has already published the ingress rule and the CNAME. It then
**rolls the route back**: it disables the route, pushes ingress without it and deletes its
CNAME, so the hostname is never left live with no policy in front of it. Fix the token in
Cloudflare (editing permissions keeps the same value), then edit the route, switch
**Enabled** back on and save. If the rollback itself fails, the message says so and tells
you to delete the DNS record in Cloudflare by hand.

The route editor's identity-provider picker and existing-token list are simply **empty**
when the token lacks the optional Read permission or Cloudflare cannot be reached. That is
not an error: an empty identity-provider selection means "every provider on the account".
