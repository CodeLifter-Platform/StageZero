# Services

Every external service this application depends on and where it's managed. Update this
file in the same change that adds, removes, or reconfigures a service. Platform-wide
map: `Platform-Standards/services/registry.md` (sibling repo,
github.com/CodeLifter-Platform/Platform-Standards).

Last reviewed: 2026-07-26.

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
