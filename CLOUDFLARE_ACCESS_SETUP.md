# Cloudflare Access Setup

A Cloudflare Tunnel makes a hostname reachable from anywhere. On its own, that is
all it does — anyone who learns the hostname can reach the service behind it.

**Cloudflare Access** is the authentication layer in front of it. Requests hit
Cloudflare's edge, Access decides whether the caller is allowed through, and only
then does the request travel down the tunnel to your service. Nothing that fails a
policy ever reaches your network.

StageZero provisions Access as part of publishing a hostname, so every route it
creates is protected by default. Opting out is a deliberate choice, not an
oversight.

---

## The four modes

Each tunnel route has an **access** section with a `mode`:

| Mode | Who gets in | Use it for |
|---|---|---|
| `identity` | People who sign in through an identity provider and match an allowed email or email domain | Anything a human uses: dashboards, admin panels, internal tools |
| `service_token` | Machines presenting a Cloudflare service token, as a client ID and secret header pair | Webhooks, CI jobs, scripts, API clients |
| `both` | Either of the above | An API a person browses and a script also calls |
| `none` | Everyone. No Access application at all | Genuinely public endpoints |

**New hostnames default to `identity`**, pre-filled with the owner email — the
`Access.OwnerEmail` setting if one is set, otherwise the first account created
during setup. Choosing `none` is explicit: the route editor warns that the service
will be on the open internet, and the routes list marks it **Public** in red.

Existing routes created before Access support was added are left on `none`. They
were already serving traffic, and silently putting a policy in front of a live
hostname would lock out whoever is using it. Switch them over one at a time.

### What gets created

For `identity`:

- An Access **application** for the hostname (`self_hosted`), carrying the session
  duration and, if you narrowed them, the allowed identity providers.
- One reusable **policy** with `allow`, including every configured email and email
  domain. The rules are ORed, so matching any one of them is enough.

For `service_token`:

- The same application.
- One reusable policy with `non_identity`, including the specific service token.
  The decision is `non_identity` rather than `allow` because the caller is a
  machine presenting credentials, not a person who logged in.

For `both`, both policies, attached in ascending precedence.

Policies are Cloudflare's current **account-level reusable** kind. The older
app-scoped policies are legacy and cannot be attached to newly created
applications.

### Identity providers

Leave the identity provider field empty and Access offers every provider
configured on your Cloudflare Zero Trust account — which is Cloudflare's own
default. That is usually what you want: a fresh account has one-time PIN over
email, and Google, GitHub and the rest are added in the Zero Trust dashboard
under **Settings → Authentication**.

Narrow the list only when a hostname should accept one specific provider.

---

## API token permissions

This is the part that most often goes wrong. StageZero's DNS and tunnel work is
**zone-scoped**; Access is **account-scoped**. A token that happily writes DNS
records may have no Access permissions whatsoever.

Add these to the token at **My Profile → API Tokens**:

| Scope | Permission | Needed for |
|---|---|---|
| Account | Access: Apps → **Edit** | Every mode except `none` |
| Account | Access: Service Tokens → **Edit** | `service_token` and `both` |
| Account | Access: Organizations, Identity Providers, and Groups → **Read** | Listing identity providers in the route editor |

Alongside the permissions the tunnel already needs:

| Scope | Permission |
|---|---|
| Account | Cloudflare Tunnel → **Edit** |
| Zone | DNS → **Edit** |
| Zone | Zone → **Read** |

StageZero checks the token before it changes anything. If a permission is missing
it refuses to provision and names the ones to add, rather than creating an
application it cannot attach a policy to.

One gap worth knowing about: the check can only prove the token can *read* those
endpoints. A token with Read but not Edit passes the check and then fails on the
first write, with Cloudflare's own error. Grant Edit, not Read.

The identity provider permission is optional. Without it the picker in the route
editor is simply empty, which means "every provider on the account" — the same
thing as leaving it unset.

---

## Service tokens and the one-time secret

A Cloudflare service token is a client ID and a client secret. Callers send them
as the `CF-Access-Client-Id` and `CF-Access-Client-Secret` headers:

```bash
curl https://app.example.com/api/health \
  -H "CF-Access-Client-Id: <client id>" \
  -H "CF-Access-Client-Secret: <client secret>"
```

**Cloudflare returns the client secret exactly once, when the token is created.**
There is no endpoint that returns it later. This is Cloudflare's design, not a
StageZero limitation.

So when StageZero mints a token, it shows the secret in a dialog that:

- cannot be dismissed by clicking outside it,
- says plainly that it will not be shown again,
- requires ticking "I have saved the client secret" before it will close.

StageZero **never** logs the secret, never writes it to the database, and never
writes it to disk. What it does keep is the client ID — which is not a secret —
the token name, and a flag recording that StageZero created it.

If the secret is lost, there is no recovery: rotate the token in the Cloudflare
Zero Trust dashboard (**Access → Service Auth**) and update every client.

### Sending the secret somewhere durable

For unattended runs, or to keep a copy in a vault, implement `IAccessSecretSink`
and replace the registration in `Program.cs`:

```csharp
builder.Services.AddScoped<IAccessSecretSink, MyVaultSink>();
```

It is called with the hostname and the minted token before the secret is returned
to the browser, so a vault keeps a copy even if the dialog is closed unread. The
default implementation records only the token name and client ID.

Implementations must not log the secret or write it to disk in plaintext, and
must let exceptions surface — a sink that fails silently would leave you believing
the secret was stored.

### Reusing a token

Turning off "Create a new token" lets you attach a token that already exists,
picked from the account or named directly. StageZero also reuses rather than
duplicates: if you ask it to create a token whose name is already taken on the
account, it adopts that one instead of minting a second.

---

## Re-running is safe

Saving a route again re-provisions it, and that is deliberately boring. Before
creating anything, StageZero looks for it:

- the **application** by its stored ID, then by the hostname,
- each **policy** by its stored ID, then by its name, which is derived from the
  hostname and so is stable across runs,
- the **service token** by its stored ID, then by its name.

Anything found is updated in place. Duplicates are not created. If something was
deleted in the Cloudflare dashboard behind StageZero's back, the next save
recreates it.

---

## When Access setup fails

Access is provisioned *after* the ingress rule and the DNS record, because it
needs the hostname to exist. That ordering creates a window where a hostname is
live with no policy in front of it, so a failure there is not just reported — the
route is rolled back:

1. The route is disabled, so the next unrelated sync does not republish it.
2. Ingress is pushed again without it.
3. Its DNS record is removed.

The error then says what failed and that the hostname is not reachable. Fix the
Access settings and save again.

If the rollback *itself* fails, the error says so explicitly and tells you to
remove the DNS record in Cloudflare by hand, because the hostname may still be
serving traffic with nothing in front of it.

---

## Removing a hostname

Deleting a route removes the tunnel ingress rule, the DNS record, the Access
application, and the reusable policies StageZero created for it. Reusable policies
outlive the application that referenced them, so they have to be deleted
explicitly or they pile up on the account.

Service tokens are treated more carefully. One is deleted **only** when both hold:

- StageZero created it, and
- no other route still references it.

Otherwise it is left in place — deleting a token an operator created elsewhere, or
one another hostname still depends on, would break something StageZero does not
own. When a token is kept, StageZero says so and why.

Teardown reports problems rather than failing the deletion. By the time it runs
the hostname no longer resolves, and a leftover Access application denies traffic
rather than allowing it, so the safe thing is to finish the removal and tell you
what needs cleaning up by hand.

---

## Checking what is actually there

The shield icon on any route opens its **Access status** page. It reads Cloudflare
directly rather than trusting StageZero's own record, so drift shows up:

- the application, its session duration, its audience tag and its identity providers,
- every attached policy with its decision and its rules,
- the attached service token, who created it, and how many routes use it.

It warns when the configured mode and reality disagree — a hostname with no
application in front of it, an application with no policies attached, a service
token that no longer exists.

---

## Troubleshooting

**"The Cloudflare API token is not allowed to …"** — the token is missing an
account-scoped Access permission. The message names it. See the table above.

**Everyone is denied** — an application with no policies attached denies every
request. The status page flags this. Save the route again to reattach.

**Nobody can sign in, but the policy looks right** — check the identity providers.
If you narrowed them to one that is not configured on the account, there is
nothing to sign in with. Clear the field to allow all of them.

**A service token gets 403** — confirm both headers are being sent, that the token
has not expired (the status page shows its expiry), and that the route's mode is
`service_token` or `both`. An `identity`-only policy does not match a token.

**The hostname is public and should not be** — the routes list marks it. Edit the
route, set a mode other than `none`, and save.
