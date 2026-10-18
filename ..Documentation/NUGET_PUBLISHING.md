# Publishing `Lifted.BlazorAuth.Basic`

The repo publishes one NuGet package, `Lifted.BlazorAuth.Basic`, from
`.github/workflows/basic-auth-nuget-publish.yml`. There is nothing to run by hand for a
pre-release: **a merge into `main` that changes `Lifted.BlazorAuth.Basic/` publishes the
next one.** A full release is the **RELEASE BASICAUTH** button. No hand-made tags, no
version bump in the csproj.

## What triggers what

| Event | Build + pack | NuGet.org | GitHub Packages | Tag + GitHub release | Release notes |
|---|---|---|---|---|---|
| Merge into `main` that changes `Lifted.BlazorAuth.Basic/` | ✅ | ✅ if `NUGET_API_KEY` is set, else a warning | ✅ | `basicauth-vX.Y.Z-pre`, prerelease | ✅ |
| **RELEASE BASICAUTH** button (`release-basicauth.yml`) | ✅ rebuild of the newest pre-release's commit | same as above | ✅ | `basicauth-vX.Y.Z`, release | ✅ |
| Merge into `main` that only touches the build files | ✅ | — | — | — | — |
| Push to any other branch, PR into `main` (library or build files changed) | ✅ | — | — | — | — |
| `workflow_dispatch` | ✅ | — | — | — | — |

Pushes that touch none of the library, `Directory.Build.props`, `Directory.Packages.props`,
`global.json`, the workflow or its version script don't run the workflow at all. Pushing a
tag never publishes anything, and the workflow must not grow a `tags:` trigger (`CLAUDE.md`).

## The version

Derived from git tags, as the platform contract requires
(`Platform-Standards/process/versioning-ci.md`), but in the library's **own namespace**:

| Product | Tags | Script |
|---|---|---|
| The StageZero app (`build.yml`) | `vX.Y.Z`, `vX.Y.Z-pre` | `.github/scripts/next-version.sh`, verbatim from the template |
| `Lifted.BlazorAuth.Basic` | `basicauth-vX.Y.Z`, `basicauth-vX.Y.Z-pre` | `.github/scripts/next-prefixed-version.sh` with `TAG_PREFIX=basicauth-v` |

`git tag --list` globs are anchored at the start, so the app's `v*` never sees a
`basicauth-v*` tag and the library never sees an app tag: a merge to one product never moves
the other's version. The release-notes collector bounds each range by the previous tag in the
same namespace for the same reason.

| Build | Version |
|---|---|
| Merge into `main` that changes the library | highest `basicauth-v*` + 1 patch, `-pre` (e.g. `0.9.74-pre`) |
| RELEASE BASICAUTH, `minor` | minor + 1, patch → 0 (`0.9.80-pre` → `0.10.0`) |
| RELEASE BASICAUTH, `major` | major + 1 (`0.9.80-pre` → `1.0.0`) |
| Anything else | next pre-release + `.<run>+<sha>`, packed without the `+sha` (not published) |

The pack step stamps the version with `-p:PackageVersion`; the `<Version>` in
`Lifted.BlazorAuth.Basic.csproj` is not what ships.

**Where it starts.** The retired `BASE_VERSION` + run-number scheme last published
`0.9.73-beta` to GitHub Packages. Until the first `basicauth-v*` tag exists the workflow
treats `0.9.73` as the highest version (`VERSION_FLOOR` in the workflow and in
`release-basicauth.yml`), so the first tag-derived version is `0.9.74-pre`, which NuGet sorts
above every package already published. Once a tag exists the floor is ignored.

**"Changes the library"** means `git diff` between the newest `basicauth-v*` tag (before the
first tag: the commit the push started from) and the merged commit shows a change under
`Lifted.BlazorAuth.Basic/`. A dependency bump in `Directory.Packages.props` alone builds the
package but does not publish it; merge a library change, or a no-op edit to it, to ship one.

## Cutting a release

GitHub → **Actions** → **RELEASE BASICAUTH** → **Run workflow**, pick `minor` or `major`.
It takes the newest `basicauth-v*-pre` tag, rebuilds **that commit** with the release
version packed in, publishes to both feeds, tags the commit `basicauth-vX.Y.Z` and creates a
GitHub release (never marked *latest*; that belongs to the app). It refuses when there is no
pre-release yet, or when the newest one has already been released.

```bash
gh workflow run release-basicauth.yml -R CodeLifter-Platform/StageZero -f bump=minor
```

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
- **Nothing reached NuGet.org after a merge** — check the run was on `main`, the version
  job's summary says *pre-release* (not "No change under Lifted.BlazorAuth.Basic/"), and the
  `publish-nuget` job ran; `--skip-duplicate` makes a re-run of an already-published version
  a silent no-op.
- **The version restarted at `0.9.74-pre` or lower** — the version job checked out without
  `fetch-depth: 0`, or the `basicauth-v*` tags were deleted. Tags are the only record.
- **RELEASE BASICAUTH says "already released"** — the newest pre-release has a release tag
  on the same commit. Merge a library change first.
