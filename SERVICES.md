# Services

Every external service this application depends on and where it's managed. Update this
file in the same change that adds, removes, or reconfigures a service. Platform-wide
map: `Platform-Standards/services/registry.md` (sibling repo,
github.com/CodeLifter-Platform/Platform-Standards).

Last reviewed: 2026-09-27.

## Cloudflare (DNS provider)

- **Usage:** The DNS provider StageZero drives for dynamic DNS updates
  (`StageZero/Models/DnsProvider.cs`, DNS config UI).
- **Managed at:** End-user Cloudflare accounts; API tokens supplied at runtime.

## Cloudflare Tunnel (public ingress)

- **Usage:** The edge layer StageZero publishes services through. The app manages
  tunnels and their ingress rules via the `cfd_tunnel` API
  (`StageZero/Services/Tunnel/CloudflareTunnelService.cs`) and writes proxied
  CNAMEs into the zone. Replaced the in-app YARP reverse proxy and Let's Encrypt
  certificate issuance, which are no longer needed — Cloudflare terminates TLS.
- **Managed at:** End-user Cloudflare accounts. The API token is supplied through
  the Tunnel Setup UI and stored encrypted; the connector token is installed on
  the host as a `cloudflared` service.
- **Detail:** [..Documentation/CLOUDFLARE_TUNNEL_SETUP.md](..Documentation/CLOUDFLARE_TUNNEL_SETUP.md).

## Cloudflare Access (Zero Trust)

- **Usage:** Every hostname StageZero publishes gets an Access application and policy;
  `service_token` routes also get a minted service token
  (`StageZero/Services/Access/CloudflareAccessService.cs`).
- **Managed at:** End-user Cloudflare accounts, same API token as DNS and Tunnel, plus the
  account-scoped Access permissions. Service-token client secrets are shown once and never
  stored.
- **Detail:** [..Documentation/CLOUDFLARE_ACCESS_SETUP.md](..Documentation/CLOUDFLARE_ACCESS_SETUP.md).

## ipify (public IP lookup)

- **Usage:** `IpMonitorService` reads the host's public IP from `https://api.ipify.org`
  on every poll. No account, no key.
- **Managed at:** Nothing to manage; if it is unreachable, IP checks fail and DNS is left
  as it is.

## NuGet.org and GitHub Packages (package publishing)

- **Usage:** CI publishes the `Lifted.BlazorAuth.Basic` package to both on pushes to `main`.
- **Managed at:** nuget.org (`NUGET_API_KEY` GitHub secret, not yet set); GitHub Packages
  uses the workflow's `GITHUB_TOKEN`.
- **Detail:** [..Documentation/NUGET_PUBLISHING.md](..Documentation/NUGET_PUBLISHING.md).

## Subscriptions API (codelifter.net, optional signups on first run)

- **Usage:** The two optional boxes on `/setup` (CodeLifter newsletter, StageZero
  update news) post once to `POST https://codelifter.net/api/subscriptions`
  (`StageZero/Services/CodeLifter/CodeLifterSubscriptions.cs`) with the email, the
  app id `stagezero`, and the app version. The site sends one double-opt-in
  confirmation email; nothing is subscribed until it is clicked. No token: the
  endpoint is public by design and rate-limited on the site. Unreachable or
  declined is a notice on the setup page, never a failed setup.
- **Managed at:** codelifter.net (CodeLifter.Net repo, `NEWSLETTER.md` → In-app
  subscriptions). The app id must exist in the site's `apps` registry. Operators
  can hide the boxes with a blank `CodeLifter__SubscriptionsUrl`.

## Release notes API (codelifter.net)

- **Usage:** `.github/workflows/release-notes.yml` pushes the pull requests each release
  shipped to `POST https://codelifter.net/api/releases` as app `stagezero`. The site curates
  them into a user-facing feature list and posts the note into StageZero's **Release Notes**
  thread in the forum. Nothing here writes release notes by hand.
- **Managed at:** the repo secret `RELEASE_NOTES_TOKEN` — this app's own push token, minted
  on the site with `npm run release-tokens -- mint stagezero` and stored in 1Password as
  `StageZero — RELEASE_NOTES_TOKEN`. Not an org secret: a token only ever writes one app's
  notes.
- **Fails soft:** no token, or an API that is down, produces a warning annotation, never a
  failed release.
- **Detail:** `Platform-Standards/process/release-notes.md`.
