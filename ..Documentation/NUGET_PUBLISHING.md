# Publishing `Lifted.BlazorAuth.Basic`

The repo publishes one NuGet package, `Lifted.BlazorAuth.Basic`, from
`.github/workflows/basic-auth-nuget-publish.yml`. There is nothing to run by hand:
**a push to `main` publishes the next version.** No tags, no version bump in the csproj.

## What triggers what

| Event | Build + test + pack | NuGet.org | GitHub Packages | Release notes |
|---|---|---|---|---|
| Push to `main` | ✅ | ✅ if `NUGET_API_KEY` is set, else a warning | ✅ | ✅ |
| `workflow_dispatch` on `main` | ✅ | same as above | ✅ | ✅ |
| Push to any other branch | ✅ | — | — | — |
| Pull request into `main` | ✅ | — | — | — |

Pushing a `v*.*.*` tag does **not** publish anything. It used to; the trigger was removed
when the repo moved to the platform versioning contract, and it must not come back
(`CLAUDE.md`).

## The version

The `version` job computes it; the pack step stamps it with `-p:PackageVersion`. The
`<Version>` in `Lifted.BlazorAuth.Basic.csproj` is not what ships.

| Build | Version |
|---|---|
| `main`, `RELEASE_LEVEL` set (today: `beta`) | `BASE_VERSION.<run number>-beta` |
| `main`, `RELEASE_LEVEL` empty | `BASE_VERSION.<run number>` |
| Any other branch | `BASE_VERSION.<run number>-pre.<run number>` (not published) |

`BASE_VERSION` (default `0.9`) and `RELEASE_LEVEL` are repository variables. This is the
retired variable-based scheme; the platform contract is now tag-derived versioning
(`Platform-Standards/process/versioning-ci.md`) and this workflow has not been migrated
yet — see *Known gaps* in [LivingSpec.md](LivingSpec.md).

## One-time setup: the NuGet.org key

Without `NUGET_API_KEY` the NuGet.org push is skipped with a warning and the build stays
green; GitHub Packages still gets the package. To enable NuGet.org:

1. On nuget.org → your account → **API Keys** → **Create**. Scope **Push new packages and
   package versions**, glob `Lifted.BlazorAuth.Basic`, and an expiry you will actually
   rotate on.
2. Store it in 1Password as `StageZero — NUGET_API_KEY` before doing anything else with it
   (`Platform-Standards/services/secrets.md`).
3. Set the repo secret:

   ```bash
   gh secret set NUGET_API_KEY -R CodeLifter-Platform/StageZero
   ```

GitHub Packages needs nothing: the job uses the workflow's `GITHUB_TOKEN` with
`packages: write`.

## Build and pack locally

```bash
dotnet build Lifted.BlazorAuth.Basic/Lifted.BlazorAuth.Basic.csproj -c Release -p:GeneratePackageOnBuild=false
dotnet pack  Lifted.BlazorAuth.Basic/Lifted.BlazorAuth.Basic.csproj -c Release --no-build -p:PackageVersion=0.0.0-local -o ./nupkgs
```

`GeneratePackageOnBuild` is on in the csproj, so a plain `dotnet build` also drops a
`0.0.1` package into `nupkgs/`. CI turns it off for the same reason. Don't push a local
package to NuGet.org — versions there can never be reused.

## Troubleshooting

- **Warning "NUGET_API_KEY secret not set"** — expected until the key exists. Not a failure.
- **401 from NuGet.org** — the key expired or lacks push scope for this package ID. Rotate it
  (and the 1Password item).
- **Nothing reached NuGet.org after a merge** — check the run was on `main` and the
  `publish-nuget` job ran; `--skip-duplicate` makes a re-run of an already-published version
  a silent no-op.
