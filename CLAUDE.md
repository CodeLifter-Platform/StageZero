# CLAUDE.md — StageZero

> **Platform standards:** this repo follows the CodeLifter harness in the sibling
> `Platform-Standards` repo (`../Platform-Standards/HARNESS.md` locally, loaded
> automatically via the folder-level CLAUDE.md symlink; otherwise
> `github.com/CodeLifter-Platform/Platform-Standards`). If that file isn't on disk —
> CI, cloud, or a lone clone — fetch it before doing UI, architecture, or CI work.
>
> **Design system:** new UI is built from the claude.ai/design project **CodeLifter Design
> System** (read it with `DesignSync`). If it doesn't have the component, token, accent, or
> pattern the work needs, stop and ask for it to be added — don't invent or approximate one.
> Rules: `Platform-Standards/design/design-system.md`.

<!-- App-specific rules only. Platform-wide standards live in the harness. -->

StageZero is a dynamic-DNS tool: a Blazor Server app (.NET 10, MudBlazor, EF Core/SQLite,
Serilog) that keeps domains pointed at the right IP addresses automatically. The repo also
publishes the `Lifted.BlazorAuth.Basic` NuGet package.

## Quick start

```bash
dotnet build StageZero.sln
dotnet run --project StageZero
```

## App-specific notes

- **This repo is PUBLIC.** Its macOS CI legs stay on GitHub-hosted runners; the
  `runs-on: macbook` rule applies to private repos only
  (`Platform-Standards/process/versioning-ci.md`).
- **NuGet publish is keyed to the computed version, not a manual tag.** The package
  version comes from the `version` job (`BASE_VERSION` + run number), same as the app
  release. Do not reintroduce a `tags:` trigger.
