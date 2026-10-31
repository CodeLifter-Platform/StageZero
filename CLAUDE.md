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

StageZero is a dynamic-DNS tool: an ASP.NET Core Web API (.NET 10, EF Core/SQLite, Serilog)
with a React + TypeScript web app (Vite, `web/`) that it serves from `wwwroot`, keeping
domains pointed at the right IP addresses automatically. It also
publishes hostnames through Cloudflare Tunnels and puts Cloudflare Access policies in front
of them. The repo also publishes the `Lifted.BlazorAuth.Basic` NuGet package.

## Quick start

```bash
dotnet build StageZero.sln
dotnet run --project StageZero                 # API on https://localhost:5001
dotnet test tests/StageZero.Tests
cd web && npm ci && npm run dev                # web app on http://localhost:5173, /api proxied
cd web && npm run lint && npm run typecheck && npm test
cd web && npm run build                        # into StageZero/wwwroot, served by the API
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
- **Web stack is a recorded exception to the Next.js/Vercel web standard**
  (`Platform-Standards/architecture/web.md`, "Recorded exceptions"): StageZero must run on
  the operator's own network, so the web app is a Vite + React SPA built into the API
  host's `wwwroot`, one container. Keep the rest of the standard: TypeScript strict, ESLint,
  Vitest + MSW, dark + light.
- **UI comes from Platform-Design, vendored verbatim** in `web/src/design` (the list is in
  `web/scripts/sync-design.mjs`). Never edit those files; `npm run design:sync` copies them
  from the sibling checkout and `npm run design:check` (CI) fails on drift. Screens import
  them through `web/src/ui.ts` only. `web/src/components/Notice.tsx` is a composition
  (Card + StatusDot) standing in for an inline alert the design system doesn't have yet.
- **Theme = `web/src/theme/stagezero.css`.** It is this app's port of
  `Platform-Standards/design/tokens.md`: it re-points the design system's `--cl-*` roles to
  the locked StageZero teal (`#14b8a6` / light `#0f766e`) and the harness's light-warm
  canvas. It is the only file in `web/` that names a colour (a test and an ESLint rule
  enforce it); views use `var(--cl-*)`. Change `tokens.md` first, then this file.
- **The API is the boundary.** Every operator route needs the session cookie
  (`StageZero/Api/Session.cs`); every non-GET needs the `X-Requested-With: StageZero`
  header; errors are problem details whose `title` is safe to show. A Cloudflare token
  goes in and never comes back out; a minted service-token secret is in exactly one
  response. Business rules live in `Services/`, never only in the web app.
- **Docs live in `..Documentation/`** (`LivingSpec.md`, `OnboardWeb.md`,
  `OnboardDocker.md`, runbooks). A change to what the app does updates `LivingSpec.md` in
  the same commit.
- **Two products, two tag namespaces, both versioned from git tags.** The app is
  `vX.Y.Z[-pre]`: `build.yml` runs `next-version.sh` (verbatim from the platform template),
  `X.Y.Z-pre` on every merge into `main`, RELEASE MINOR / RELEASE MAJOR via `promote.yml`.
  Its artifact is the container image (amd64 + arm64) on
  `ghcr.io/codelifter-platform/stagezero`, mirrored to Docker Hub when `DOCKERHUB_*` are
  set. The NuGet library is `basicauth-vX.Y.Z[-pre]`: `basic-auth-nuget-publish.yml` runs
  `next-prefixed-version.sh` (`TAG_PREFIX=basicauth-v`), publishes a pre-release only on
  merges that change `Lifted.BlazorAuth.Basic/`, and RELEASE BASICAUTH
  (`release-basicauth.yml`) promotes it. Neither script may read the other's tags. Never
  create tags by hand and never add a `tags:` trigger.
- **Two databases.** SQLite (default) or PostgreSQL via `STAGEZERO_DB_PROVIDER` /
  `STAGEZERO_DB_CONNECTION`, or via the Database page, which stores the choice encrypted in
  `database.json` in the data directory (`Data/DatabaseSettingsFile.cs`; the variables win
  over it, and a switch is a restart). A model change needs a migration for **each**:
  `dotnet ef migrations add <Name> --project StageZero --context ApplicationDbContext` and
  `... --context PostgresApplicationDbContext --output-dir Data/Migrations/Postgres`
  (move the Postgres snapshot if EF drops it in a nested `StageZero/StageZero/`). Run the
  suite on Postgres with `STAGEZERO_TEST_POSTGRES=<server connection string>`.
