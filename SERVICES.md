# Services

Every external service this application depends on and where it's managed. Update this
file in the same change that adds, removes, or reconfigures a service. Platform-wide
map: `Platform-Standards/services/registry.md` (sibling repo,
github.com/CodeLifter-Platform/Platform-Standards).

Last reviewed: 2026-09-27.

## PostgreSQL (optional database)

- **Usage:** Where the data goes instead of the SQLite file, when chosen in the web app
  (Settings → Database, or the first-run page; saved encrypted in `database.json` in the data
  directory) or by `STAGEZERO_DB_PROVIDER=postgres` (`StageZero/Data/DatabaseChoice.cs`,
  `DatabaseSettings.cs`; migrations in `StageZero/Data/Migrations/Postgres/`). SQLite stays
  the default.
- **Managed at:** The operator's own server outside StageZero's stack: for a CodeLifter
  install, the platform Infra stack, with a `stagezero` role and database from
  `Infra/tools/create-app-db.sh prod stagezero` and the password in 1Password as
  `stagezero — INFRA_POSTGRES_PASSWORD`. `docker-compose.postgres.yml` runs one inside the
  stack for CI and trials only. The password is never logged or returned by the API.

## GitHub Container Registry (image publishing)

- **Usage:** Every merge into `main` and every release button pushes the multi-arch image to
  `ghcr.io/codelifter-platform/stagezero` (`.github/workflows/build.yml`, `publish`), signed
  with cosign (keyless, Sigstore) and with a provenance attestation.
- **Managed at:** The CodeLifter-Platform GitHub org's Packages. Authenticates with the
  workflow's own `GITHUB_TOKEN`; no secret to keep. The package must be public (set once,
  after the first push, in the package's settings).

## Docker Hub (image mirror)

- **Usage:** The same image and tags, mirrored for people who look on Docker Hub. Skipped
  with a warning until configured.
- **Managed at:** A Docker Hub organisation. Repo secrets `DOCKERHUB_USERNAME` and
  `DOCKERHUB_TOKEN` (an access token with read/write, never the account password) and the
  repo variable `DOCKERHUB_IMAGE` (e.g. `codelifter/stagezero`). 1Password items:
  `StageZero — DOCKERHUB_USERNAME`, `StageZero — DOCKERHUB_TOKEN`. Docker's Sponsored Open
  Source program lifts the anonymous pull limit.

## Cloudflare (DNS provider)

- **Usage:** The DNS provider StageZero drives for dynamic DNS updates
  (`StageZero/Models/DnsProvider.cs`, DNS config UI).
- **Managed at:** End-user Cloudflare accounts. One API token, supplied on the Settings
  page and stored encrypted, serves DNS, Tunnel and Access.

## Public IP lookup (Cloudflare trace, ipify, AWS checkip)

- **Usage:** Every IP check asks all three for this network's public IPv4 address and
  acts only when two agree (`StageZero/Services/IpMonitoring/PublicIpResolver.cs`):
  `https://1.1.1.1/cdn-cgi/trace`, `https://api.ipify.org`,
  `https://checkip.amazonaws.com`.
- **Managed at:** Nothing to manage — public, unauthenticated, no account or key. Any one
  can be down or wrong without effect; if fewer than two answer, the check is skipped.

## Cloudflare Tunnel (public ingress)

- **Usage:** The edge layer StageZero publishes services through. The app manages
  tunnels and their ingress rules via the `cfd_tunnel` API
  (`StageZero/Services/Tunnel/CloudflareTunnelService.cs`) and writes proxied
  CNAMEs into the zone. Replaced the in-app YARP reverse proxy and Let's Encrypt
  certificate issuance, which are no longer needed — Cloudflare terminates TLS.
- **Managed at:** End-user Cloudflare accounts. The API token is the one saved on the
  Settings page; the connector token is installed on the host as a `cloudflared` service.
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

- **Usage:** CI publishes the `Lifted.BlazorAuth.Basic` package to both on merges into
  `main` that change `Lifted.BlazorAuth.Basic/` (a pre-release), and from the RELEASE
  BASICAUTH button (a release).
- **Managed at:** nuget.org (`NUGET_API_KEY` GitHub secret, not yet set); GitHub Packages
  uses the workflow's `GITHUB_TOKEN`.
- **Detail:** [..Documentation/NUGET_PUBLISHING.md](..Documentation/NUGET_PUBLISHING.md).

## codelifter.net (install registration, optional email signups)

- **Usage:** Two calls, both to the site named by `CodeLifter__SubscriptionsUrl`.
  - **Registration** at first run, unless the operator unticks *Register this install
    with codelifter.net*: `POST https://codelifter.net/api/app-users/register` with the
    admin email, the app id `stagezero` and the version
    (`StageZero/Services/CodeLifter/CodeLifterRegistration.cs`). It records who runs
    StageZero, subscribes nobody and makes the site send nothing. Best-effort: a failure
    never fails setup.
  - **Email signups** on Settings → Email from CodeLifter: the newsletter and StageZero
    updates, each its own `POST https://codelifter.net/api/subscriptions` for the
    signed-in address (`CodeLifterSubscriptions.cs`). The site sends one double-opt-in
    confirmation email per request; nothing is subscribed until it is clicked.
  - **Manage email preferences** on the same card: `POST
    https://codelifter.net/api/subscriptions/manage-link` with the signed-in address
    (`CodeLifterManageLink.cs`). The site emails a one-hour link to
    `codelifter.net/newsletter/manage`, where each list switches on or off. StageZero
    stores no credential for it.
  - No token on any of them: both endpoints are public by design and rate-limited on the site.
- **Managed at:** codelifter.net (CodeLifter.Net repo, `NEWSLETTER.md` → App users, In-app
  subscriptions and Managing lists from an emailed link). The app id must exist in the site's `apps` registry. A blank
  `CodeLifter__SubscriptionsUrl` switches both off.

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
