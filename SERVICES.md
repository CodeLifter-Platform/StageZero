# Services

Every external service this application depends on and where it's managed. Update this
file in the same change that adds, removes, or reconfigures a service. Platform-wide
map: `Platform-Standards/services/registry.md` (sibling repo,
github.com/CodeLifter-Platform/Platform-Standards).

Last reviewed: 2026-08-26.

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
- **Detail:** [CLOUDFLARE_TUNNEL_SETUP.md](CLOUDFLARE_TUNNEL_SETUP.md).

## NuGet.org (package publishing)

- **Usage:** Publishes the client library package from CI.
- **Managed at:** nuget.org; `NUGET_API_KEY` GitHub secret.
- **Detail:** [GITHUB_ACTIONS_SETUP.md](GITHUB_ACTIONS_SETUP.md).

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
