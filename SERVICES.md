# Services

Every external service this application depends on and where it's managed. Update this
file in the same change that adds, removes, or reconfigures a service. Platform-wide
map: `Platform-Standards/services/registry.md` (sibling repo,
github.com/CodeLifter-Platform/Platform-Standards).

Last reviewed: 2026-07-18.

## Cloudflare (DNS provider)

- **Usage:** The DNS provider StageZero drives for dynamic DNS updates
  (`StageZero/Models/DnsProvider.cs`, DNS config UI).
- **Managed at:** End-user Cloudflare accounts; API tokens supplied at runtime.

## NuGet.org (package publishing)

- **Usage:** Publishes the client library package from CI.
- **Managed at:** nuget.org; `NUGET_API_KEY` GitHub secret.
- **Detail:** [GITHUB_ACTIONS_SETUP.md](GITHUB_ACTIONS_SETUP.md).
