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
| **API token** | Yes | You create it in the Cloudflare dashboard | **DNS Configuration → Cloudflare credentials**, and **Tunnel Setup → API token** | Yes, encrypted with ASP.NET Data Protection |
| **Account ID** | No | Cloudflare dashboard | **Tunnel Setup → Account ID** | Yes, plain |
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
| **Dynamic DNS** | Zone | Zone → **Read** | Lists zones on the DNS Configuration credentials card (`GET /zones`) |
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

### One token or two?

The DNS provider and Tunnel Setup each keep their own copy of a token, so you choose:

- **One token for everything.** Grant every permission in the table. Paste the same token
  into DNS Configuration and into Tunnel Setup. Simplest, and what the Tunnel Setup page
  suggests.
- **Least privilege.** A *dynamic DNS* token with only Zone → Read and DNS → Edit, limited
  to the zones you update, for each DNS provider. A *tunnel* token with everything else for
  Tunnel Setup. Access always uses the Tunnel Setup token, since Access is provisioned as
  part of publishing a route.

| | DNS provider token | Tunnel Setup token |
|---|---|---|
| Zone → Zone → Read | ✅ | ✅ |
| Zone → DNS → Edit | ✅ | ✅ |
| Account → Cloudflare Tunnel → Edit | | ✅ |
| Account → Access: Apps and Policies → Edit | | ✅ (unless every route is `none`) |
| Account → Access: Service Tokens → Edit | | ✅ (for `service_token` / `both`) |
| Account → Access: Organizations, Identity Providers, and Groups → Read | | optional |

If you only want dynamic DNS and tunnels, leave the Access permissions off and set each
route's Access mode to `none`. Every other part of the app works without them. Routes
default to `identity`, though, and saving one without Access permissions fails and rolls
back.

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

Not a secret, but Tunnel Setup needs it. It appears in the dashboard under
**Workers & Pages → Account details**, and in the **API** section on the right of any
zone's **Overview** page.

---

## Where the token goes in StageZero

**Dynamic DNS.** **DNS Configuration → Cloudflare credentials**. Paste the token into
*API token* and click **Connect**. StageZero lists every zone the token can reach; click
**Add provider** next to each one you want (zones already added read *Added*). The provider
is named after its zone. One provider per zone. Each provider stores its own copy of the
token.

**Tunnel and Access.** **Tunnel Setup → 1. Cloudflare credentials**. Paste the token and
the Account ID and click **Connect**. StageZero lists the zones and tunnels the token can
see. The token is saved only when you **Create tunnel** or **Adopt** one. Until then it
travels in each request and is not stored.

In both places the token is encrypted with ASP.NET Data Protection before it reaches the
database, and it is never sent back to the browser. The keys that decrypt it live in the
data directory (`dp-keys/`, `/app-data/dp-keys` in Docker). Lose that directory and every
stored token is unreadable; StageZero then behaves as if the tunnel was never set up, and
DNS updates fail until the providers are re-added.

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
2. **Tunnel Setup.** Paste the new token and the Account ID and click **Connect**. Pick the
   same zone, then **Adopt** the tunnel StageZero already manages (it is listed as
   *Remote*). This saves the new token and re-pushes every route's ingress rules. It does
   not change the tunnel ID, the CNAMEs or the connector token, so `cloudflared` keeps
   running and nothing goes offline. Avoid **Disconnect** for this. It is not needed, and
   it leaves StageZero managing nothing until you connect again.
3. **Each DNS provider.** A provider's token cannot be edited in place yet. Delete the
   provider, then add it again from **Cloudflare credentials** with the new token, then **Import all records**. Deleting a
   provider removes its records *from StageZero only*; Cloudflare keeps them, and the
   import brings them back. Records you added by hand need **Auto-update** ticked again if
   the import did not set it (it does for A records).
4. **Revoke the old token** in Cloudflare (*Delete* on the old token, unless you used
   *Roll*, which already invalidated it).
5. **Check.** On **IP Monitor**, click **Check now**; the server log should show no
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

### DNS Configuration

These messages do not carry Cloudflare's status code. The server log does: look for
`Error getting Cloudflare zones` or `Error getting Cloudflare DNS records`, followed by
`Cloudflare list failed: <status>`.

| Message | Missing |
|---|---|
| `Failed to load zones: Could not get zones` (Connect on DNS Configuration) | The token is wrong, revoked or expired (Unauthorized in the log), or lacks Zone → Zone → Read (Forbidden) |
| `No zones found for this API token` | The token works but no zone is in its *Zone Resources* |
| `Failed to load DNS records: Could not get DNS records` (Load existing records) | Zone → DNS (Read at least; grant Edit), or the token was revoked |
| `Error importing records: Could not get DNS records` (Import all records) | Same |

Auto-updates run in the background, so their failures go to the **server log**, not the
screen:

- `Failed to update Cloudflare DNS record` followed by `Failed to verify DNS record <name>`:
  the token can read records but not write them. Grant DNS → Edit.
- `Could not list DNS records for provider <name>`: the token cannot read the zone's
  records at all, or has been revoked.

### Tunnel Setup

| Message | Missing |
|---|---|
| `Could not reach Cloudflare with those credentials: Could not get zones` | The token is wrong, revoked or expired, or lacks Zone → Zone → Read (the log has the status) |
| `Could not reach Cloudflare with those credentials: Cloudflare API error while trying to list tunnels: Forbidden — ‹…›` | Account → Cloudflare Tunnel → Edit, a wrong Account ID, or the account is not in the token's *Account Resources* |
| `The token is valid but can't see any zones. Check that it has Zone → Zone → Read.` | Zone → Zone → Read, or no zone in *Zone Resources* |
| `That zone isn't visible to this token.` | The chosen zone is not in the token's *Zone Resources* |
| `Could not create the tunnel: …` / `Could not adopt the tunnel: …` | Cloudflare Tunnel → **Edit** (Read is not enough) |
| `Could not fetch the connector token: …` | Cloudflare Tunnel → Edit, or the tunnel was deleted in Cloudflare |

### Tunnel Routes

| Message | Missing |
|---|---|
| `Could not save the route: Cloudflare API error while trying to write CNAME for <host>: Forbidden — ‹…›` | Zone → DNS → Edit on the tunnel's zone |
| `Could not save the route: Cloudflare API error while trying to sync ingress rules: …` | Cloudflare Tunnel → Edit |
| `Could not save the route: Cloudflare Access setup failed for <host>: The saved Cloudflare API token cannot manage Access. Add these account-scoped permissions and save the token again: Access: Apps Read and Access: Apps Edit; Access: Service Tokens Read and Access: Service Tokens Edit. The route has been disabled and its DNS record removed, …` | The Access permissions named. The service-token one is listed whenever it is missing but only blocks `service_token` and `both` routes |
| `Could not save the route: Cloudflare Access setup failed for <host>: ‹…› The route has been disabled and its DNS record removed, …` (any other Cloudflare detail) | Usually an Edit level that is only Read, or the policy permission (see the note above) |
| `Cloudflare Tunnel is not configured. Complete setup at /tunnel-settings first.` | Not a permission problem. Tunnel Setup was never finished, or its token can no longer be decrypted (lost `dp-keys`) |

When Access fails, StageZero has already published the ingress rule and the CNAME. It then
**rolls the route back**: it disables the route, pushes ingress without it and deletes its
CNAME, so the hostname is never left live with no policy in front of it. Fix the token in
Cloudflare (editing permissions keeps the same value), then edit the route, switch
**Enabled** back on and save. If the rollback itself fails, the message says so and tells
you to delete the DNS record in Cloudflare by hand.

The route editor's identity-provider picker and existing-token list are simply **empty**
when the token lacks the optional Read permission or Cloudflare cannot be reached. That is
not an error: an empty identity-provider selection means "every provider on the account".
