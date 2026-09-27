# StageZero — Public Release Audit

Audit date: **2026-09-21** (findings) · rebased and re-scoped **2026-09-27** on `main`
after the auth rework (#16 design system, #17 local-only sign-in) · branch
`claude/public-release-audit`

A working checklist. Each item is ticked in the same commit that fixes it. When an item
changes what the app does, `LivingSpec.md` is updated in that commit too. Items closed by
an owner decision rather than a fix say so.

**Verdict (2026-09-27):** the app is sound for a self-hosted install — setup and sign-in
work without email, the security findings are fixed or decided, DDNS is correct and
tested — but it is **not yet publishable**: nothing builds, tests or releases it in CI, and
no container image is pushed (1.5, 1.6). Those two are the remaining blockers.

## How this was audited

- 2026-09-21: built the solution and the app; read the code and the real logs under
  `StageZero/logs/`; ran the app against an **isolated, empty data directory** and drove it
  in Chrome as an anonymous visitor. Signed-in pages, real Cloudflare calls and the Docker
  build were read, not exercised.
- 2026-09-27: every fix re-applied onto the reworked `main` and verified by the test suite
  (181 tests, hosting the real app in memory with the network faked). The container was
  not built (no Docker daemon in the audit environment); 2.7 and 4.2's `HEALTHCHECK` are
  untested in a real container.

> **Safety note for anyone reproducing this:** never `dotnet run` against the default data
> directory on a dev machine. `~/Library/Application Support/StageZero/stagezero.db` holds
> real providers, and the IP monitor rewrites any auto-update record that doesn't match the
> *current machine's* public IP within seconds of startup.

## Progress

| Phase | Items | Done | Open |
|---|---|---|---|
| 1 · Install and log in | 6 | 4 | 1.5 CI, 1.6 image |
| 2 · Security | 8 | 8 | — |
| 3 · DDNS correctness | 6 | 6 | — |
| 4 · Hygiene and docs | 9 | 9 | — |
| 5 · Design system rebuild | 5 | 4 | 5.5 (partly) |
| 6 · Killer-app features | 10 | 0 | all |
| 7 · Marketing screenshots | 7 | 0 | all |

---

## Phase 1 · A new user can install it and log in

- [x] **1.1 Solution builds.** `StageZero.sln` referenced a project that didn't exist
  (MSB3202). Fixed on `main` before this branch.

- [x] **1.2 Auth pages load directly.** `/login`, `/setup`, `/forgot-password`,
  `/reset-password` returned 404 on a cold request (`MapRazorComponents` never added the
  library assembly). Fixed on `main` (#17); pinned by `AuthPageRoutingTests`.

- [x] **1.3 Login survives a refresh — decided: not for this release.** The signed-in
  user lives on the scoped `AuthService`, so a reload lands on `/login`. The original fix
  (cookie authentication) was built on the old branch and dropped: it widens the NuGet
  library's surface and pulls 2FA and cookie-ticket handling with it. The owner chose a
  local-only sign-in model for this release; the gap is recorded in `LivingSpec.md`
  (*Partial*, *Known gaps*).

- [x] **1.4 First-run setup completes without SMTP.** Superseded by #17: StageZero never
  sends email. `/setup` creates the first admin from one form with no verification step;
  `/forgot-password` writes its code to the server log as a banner and the page says so.

- [ ] **1.5 CI builds, tests and releases the app.** Still true: `basic-auth-nuget-publish.yml`
  packs the library only, nothing builds or tests StageZero itself in CI, and the
  `dotnet test` step is `continue-on-error` (the suite now exists and passes: 181 tests).
  **Conflict to settle first:** the owner decision below adopts the harness tag scheme, but
  the repo `CLAUDE.md` says the NuGet version is `BASE_VERSION` + run number and *not* to
  reintroduce a tag trigger. One of the two has to change before a `build.yml` is written.
  Done when: a push builds and tests the app, and a merge to `main` produces a release.

- [ ] **1.6 Published container image.** No image is built or pushed anywhere, so the
  public has no install path short of cloning and building. Fix: multi-arch
  (`linux/amd64` + `linux/arm64`) image to `ghcr.io/codelifter-platform/stagezero` from the
  release job (after 1.5); README gets a one-line `docker run` and a minimal compose file.
  Done when: `docker run ghcr.io/...` on a Raspberry Pi reaches `/setup`.

## Phase 2 · Security

- [x] **2.1 Brute-forceable password reset and login.** Codes now come from
  `RandomNumberGenerator`, compare in constant time, and are void after 5 wrong guesses;
  5 wrong passwords lock the account for 15 minutes; a per-address window (10 per 15 min)
  throttles failed sign-ins and every reset request. `reset-password` on the server is the
  recovery path when the logged code is out of reach. `BruteForceTests` proves the 6th
  guess fails even when right.

- [x] **2.2 Secrets in logs — decided: codes are logged on purpose.** With no email, the
  server log *is* the delivery channel for reset codes (#17): whoever can read the log
  controls the server, which is who may reset the admin. What changed: the code is written
  once, as a Warning-level banner with a fixed heading, by `ServerLogCodeService` — never
  by the auth library's debug logging — and 2.1 limits what a leaked code is worth.

- [x] **2.3 Cloudflare DNS token stored in plaintext.** `DnsProvider` now stores
  `ProtectedApiToken` through `CloudflareTokenProtector` (the same Data Protection ring as
  the tunnel token); a plaintext token from an older install is encrypted on first start.
  `DnsTokenStorageTests` reads the raw row and the decrypted value.

- [x] **2.4 Public IP shown to anonymous visitors.** The IP chip is its own component,
  rendered (and loading the IP) only for a signed-in user; the navigation drawer is gated
  the same way. `AnonymousChromeTests`: the sign-in pages, setup and a cold request to a
  protected page carry neither the recorded IP nor the nav links.

- [x] **2.5 Two-factor login — decided: not for this release.** TOTP with recovery codes
  was built on the old branch on top of cookie sign-in and dropped with it (1.3). Recorded
  as a known gap; revisit with 1.3.

- [x] **2.6 Vulnerable SQLite native package.** EF Core 10.0.12 / `SQLitePCLRaw` patched;
  the build is warning-free with `TreatWarningsAsErrors`.

- [x] **2.7 Container runs as root.** The `release` stage now runs as `$APP_UID` (1654)
  and `/app-data` is owned by that user in the image. **Untested in a real container**
  (no Docker daemon here). Existing installs: a root-owned volume needs `chown -R 1654`
  once — documented in `OnboardDocker.md`. Done when confirmed: `docker exec … id` is
  non-root and the database writes.

- [x] **2.8 Data Protection configured twice.** One `AddDataProtection()` on `dp-keys/`;
  a legacy `keys/` ring is copied in on first start so an older install's tunnel token
  still decrypts (`DataProtectionKeysTests`).

## Phase 3 · DDNS correctness

- [x] **3.1 Updates strip the Cloudflare proxy.** The update is a `PATCH` of `content`
  alone; proxy, TTL, comment and tags are untouched (`DnsUpdateTests`).

- [x] **3.2 Dead IP-change handler.** `IpChangeHandlerService` and `DnsUpdateService` are
  gone; a singleton `IIpChangeNotifier` carries changes to the header chip and the IP
  Monitor page (`IpChangeNotificationTests`). `DnsVerificationService` on every check is
  what updates Cloudflare.

- [x] **3.3 AAAA auto-update cannot work.** Only A records are auto-updated
  (`DnsRecord.SupportsAutoUpdate`); the dialogs, the import and the verifier apply it, and
  a data migration cleared the flag on existing AAAA/CNAME rows (`RecordTypeTests`).

- [x] **3.4 Single IP source, no validation.** `PublicIpResolver` asks Cloudflare trace,
  ipify and AWS checkip in parallel; only a public IPv4 counts; two must agree to change
  the IP, a lone answer can only confirm it; otherwise nothing is recorded
  (`PublicIpResolverTests`).

- [x] **3.5 No Cloudflare pagination.** Every page is read; each zone is listed once per
  check and matched in memory; the record ID is taken from the listing
  (`PaginationTests`: a record on page 2 of a 150-record zone gets the PATCH).

- [x] **3.6 IP history never pruned.** Each row is a run (first seen, last confirmed,
  count); an unchanged check extends the row. A migration folded the old history into runs
  (`IpHistoryTests`).

## Phase 4 · Hygiene and docs

- [x] **4.1 No error page.** `/Error` (server-rendered, anonymous, shows a trace reference
  and never the exception) behind `UseExceptionHandler` outside Development
  (`ErrorPageTests`).

- [x] **4.2 Health endpoint.** `/healthz` (database, IP-check freshness → Degraded, still
  200) and a `HEALTHCHECK` that runs `dotnet StageZero.dll healthcheck` (`HealthTests`).
  The Dockerfile change is untested in a real container.

- [x] **4.3 Real migrations.** EF Core migrations with a baseline that adopts a database
  from before migrations existed; `Program.cs` has no DDL (`DatabaseInitializerTests`).

- [x] **4.4 Production log level.** Information outside Development, `STAGEZERO_LOG_LEVEL`
  overrides, framework chatter at Warning (`LoggingSetupTests`).

- [x] **4.5 Third-party assets.** Gone with #16 (bundled fonts, no kit); pinned by
  `SelfContainedAssetsTests`.

- [x] **4.6 Home page claims.** Cards say Dynamic DNS, Cloudflare Tunnel, and what
  "secure" means here; no multi-provider claim.

- [x] **4.7 LivingSpec is wrong.** Rewritten with #16/#17 and kept current in this branch.

- [x] **4.8 Repo leftovers.** `nupkgs/` and `src/Quip` deleted here; the docs moves and
  `.env.example` cleanup landed with #16.

- [x] **4.9 Compiler warnings and build props.** `Directory.Build.props`
  (`TreatWarningsAsErrors`) and `Directory.Packages.props`; 0 warnings.

## Phase 5 · Design system rebuild

Done by #16 except where noted. New UI still starts from `Platform-Design` (readme, tokens,
the component's `.prompt.md`) before any markup.

- [x] **5.1 Locked accent.** StageZero teal (`#14b8a6` / light `#0f766e`) in
  `Platform-Standards/design/app-accents.md` — *not* the `#34d399` first decided; the brand
  marks in `Assets/svg/` still use the old green (known gap).
- [x] **5.2 Token-driven `MudTheme`** (`StageZeroTheme.cs`), Inter + JetBrains Mono bundled.
- [x] **5.3 Remove raw colours.** Views use `Color.*` / `--mud-palette-*` only.
- [x] **5.4 Persist the theme choice** (`localStorage`, applied on first interactive render).
- [ ] **5.5 Layout defects.** Drawer clip fixed (`DrawerClipMode.Always`) and the icon-only
  drawer has tooltips. Not checked: form buttons enabling only on blur rather than as you
  type.

## Phase 6 · Killer-app features

Ordered by value for a home-infrastructure user. None started.

- [ ] **6.1 Notifications** — IP changed, DNS update failed, tunnel down — via ntfy,
  Discord and generic webhook. `IIpChangeNotifier` (3.2) is the hook for the first.
- [ ] **6.2 Real dashboard** replacing the marketing Home page: current IP, tunnel
  connector status, per-record sync state, last change, recent errors.
- [ ] **6.3 Service health per tunnel route** — is `jellyfin.example.com` up, and how fast.
- [ ] **6.4 IPv6 / AAAA** with separate v4 and v6 detection (builds on 3.3 and 3.4).
- [ ] **6.5 One-click Cloudflare Access on a route** — largely exists (Access is on by
  default for new routes, `Services/Access/`); what's left is the UX pass.
- [ ] **6.6 Import existing routes when adopting a tunnel.**
- [ ] **6.7 Multiple zones per tunnel and wildcard hostnames.**
- [ ] **6.8 Audit log of every DNS and tunnel change.**
- [ ] **6.9 Config backup / export and restore.**
- [ ] **6.10 Cloudflare-native positioning.** Copy done (4.6); the `ProviderType` plumbing
  that implies a second provider is still in the model.

## Phase 7 · Marketing screenshots

Capture only after Phase 5 (done) and with documentation addresses (`203.0.113.x`) and
example domains, never real ones.

- [ ] **7.1 Tunnel Routes table** — populated, enabled chips. The hero shot.
- [ ] **7.2 Tunnel setup, connector-token step.**
- [ ] **7.3 IP Monitor** — large current IP, "Check Now", history with a real change in it.
- [ ] **7.4 DNS Configuration** — records with sync status and last-updated times.
- [ ] **7.5 Dark and light side by side** on the routes page.
- [ ] **7.6 First-run setup** (one form, with the optional signup boxes).
- [ ] **7.7 The new dashboard** (after 6.2).

---

## Owner decisions

**2026-09-21**

1. **Accent:** lock `#34d399` (5.1). *Superseded 2026-09-26 by #16: StageZero teal.*
2. **Versioning:** adopt the harness tag scheme (`next-version.sh`, `-pre` on merge,
   RELEASE MINOR / MAJOR buttons) for the app and the NuGet package; the repo `CLAUDE.md`
   is updated to match (1.5). *Not done; `CLAUDE.md` still says the opposite — see 1.5.*
3. **Deletions (4.8):** delete `StageZero.ReverseProxy/`, `nupkgs/`, `src/Quip`, and the
   local `StageZero/stagezero.db*` and `StageZero/logs/`. Keep the hosting guide, moved
   into `..Documentation/`. *Done.*
4. **DNS providers (6.10):** Cloudflare-native. No second provider. *Copy done.*

**2026-09-27**

5. **Sign-in is local-only.** No email, ever: setup is one form, reset codes go to the
   server log as an unmissable banner, and the pages say where to look. Optional
   codelifter.net signups on the setup form (#17).
6. **No cookie sign-in and no two-factor in this release** (1.3, 2.5). Both were built on
   the old audit branch and dropped when it was rebased onto the reworked auth.
7. **The audit branch was rebuilt from `main`**, one fix at a time, rather than merged:
   29 commits behind and conflicting on every auth file. Each port was re-verified by the
   suite; what didn't apply any more (cookie auth, 2FA, SMTP-less setup, "never log codes")
   was dropped and is listed above as a decision.

## What's left before a public release

1. Settle the versioning rule (decision 2 vs `CLAUDE.md`), then **1.5** a `build.yml`
   that builds, tests and releases the app.
2. **1.6** the GHCR image from that job, and a `docker run` line in the README.
3. Build the image once and confirm **2.7** and **4.2** in a real container.
4. Phase 7 screenshots; Phase 6 as product work after release.
