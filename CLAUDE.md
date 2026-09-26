# CLAUDE.md — StageZero

> **Platform standards:** this repo follows the CodeLifter harness in the sibling
> `Platform-Standards` repo (`../Platform-Standards/HARNESS.md` locally, loaded
> automatically via the folder-level CLAUDE.md symlink; otherwise
> `github.com/CodeLifter-Platform/Platform-Standards`). If that file isn't on disk —
> CI, cloud, or a lone clone — fetch it before doing UI, architecture, or CI work.
>
> **Design system:** new UI is built from the **CodeLifter Design System** in the sibling
> `Platform-Design` repo (`../Platform-Design/readme.md` locally; otherwise
> `github.com/CodeLifter-Platform/Platform-Design`). Read `readme.md`, then `tokens/`, then
> the component's `.prompt.md` — before any markup. If it doesn't have the component, token,
> accent, or pattern the work needs, stop and ask for it to be added — don't invent or
> approximate one. Rules: `Platform-Standards/design/design-system.md`.

<!-- App-specific rules only. Platform-wide standards live in the harness. -->

StageZero is a dynamic-DNS tool: a Blazor Server app (.NET 10, MudBlazor, EF Core/SQLite,
Serilog) that keeps domains pointed at the right IP addresses automatically. It also
publishes hostnames through Cloudflare Tunnels and puts Cloudflare Access policies in front
of them. The repo also publishes the `Lifted.BlazorAuth.Basic` NuGet package.

## Quick start

```bash
dotnet build StageZero.sln
dotnet run --project StageZero
dotnet test StageZero.Tests/StageZero.Tests.csproj
```

## App-specific notes

- **This repo is PUBLIC.** Its macOS CI legs stay on GitHub-hosted runners; the
  `runs-on: macbook` rule applies to private repos only
  (`Platform-Standards/process/versioning-ci.md`).
- **Cloudflare Access is on by default for new tunnel routes.** Access is account-scoped
  while DNS is zone-scoped, so the API token needs `Access: Apps Edit` and
  `Access: Service Tokens Edit` on top of the tunnel and DNS permissions
  (`..Documentation/CLOUDFLARE_ACCESS_SETUP.md`). A minted service token's client secret
  is returned by Cloudflare exactly once — never log it, store it, or write it to disk.
- **Theme = `StageZero/Application/Theme/StageZeroTheme.cs`.** It is this app's port of
  `Platform-Standards/design/tokens.md` (light = warm paper) with the locked StageZero teal
  accent (`#14b8a6` / light `#0f766e`). Views use MudBlazor `Color.*` and
  `--mud-palette-*` only, never a hex; mono text uses the `sz-mono` class. Change
  `tokens.md` first, then this file.
- **Docs live in `..Documentation/`** (`LivingSpec.md`, `OnboardWeb.md`,
  `OnboardDocker.md`, runbooks). A change to what the app does updates `LivingSpec.md` in
  the same commit.
- **NuGet publish is keyed to the computed version, not a manual tag.** The package
  version comes from the `version` job (`BASE_VERSION` + run number), same as the app
  release. Do not reintroduce a `tags:` trigger.
