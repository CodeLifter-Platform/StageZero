# StageZero — Public Release Audit

Audit date: **2026-09-21** · Branch audited: `feat/release-notes` @ `f9f283b`

A working checklist. Each item is ticked in the same commit that fixes it. When an item
changes what the app does, `LivingSpec.md` is updated in that commit too.

**Verdict:** not ready for public release. Dynamic DNS updates work, but setup and login
break for a new user, and security is too weak for an app exposed to the internet.

## How this was audited

- Built the solution and the app project; read the code and the real logs under
  `StageZero/logs/`.
- Ran the app against an **isolated, empty data directory** (fake `HOME`, no `.env` in the
  search path) and drove it in Chrome as an anonymous visitor.
- **Not exercised live:** the signed-in pages (needs account creation and a password),
  real Cloudflare API calls, and the Docker image build. Findings in those areas are from
  reading the code and are marked *(code)*.

> **Safety note for anyone reproducing this:** never `dotnet run` against the default data
> directory on a dev machine. `~/Library/Application Support/StageZero/stagezero.db` holds
> real providers, and the IP monitor rewrites any auto-update record that doesn't match the
> *current machine's* public IP within seconds of startup.

## Progress

| Phase | Items | Done |
|---|---|---|
| 1 · Install and log in | 6 | 1 |
| 2 · Security | 8 | 1 |
| 3 · DDNS correctness | 6 | 0 |
| 4 · Hygiene and docs | 9 | 1 |
| 5 · Design system rebuild | 5 | 0 |
| 6 · Killer-app features | 10 | 0 |
| 7 · Marketing screenshots | 7 | 0 |

---

## Phase 1 · A new user can install it and log in

- [x] **1.1 Solution builds.** `dotnet build StageZero.sln` fails with MSB3202:
  `StageZero.sln` references `Lifted.BlazorAuth.AspNetIdentity`, which doesn't exist.
  *Verified: ran it.* Fix: remove the project entry (and its config rows) from the `.sln`.
  Done when: the README quick-start command succeeds on a fresh clone.

- [ ] **1.2 Auth pages load directly.** `/login`, `/setup`, `/forgot-password`,
  `/reset-password` return 404 on direct load, refresh or bookmark; they only work via
  in-app navigation. *Verified: curl against the running app.* Cause: `Program.cs:449`
  `MapRazorComponents` never calls `.AddAdditionalAssemblies(typeof(Login).Assembly)`.
  Done when: all four URLs return 200 on a cold request.

- [ ] **1.3 Login survives a refresh.** The signed-in user is a private field on the
  scoped `AuthService` (`Lifted.BlazorAuth.Basic/Services/AuthService.cs:48`), so it dies
  with the Blazor circuit; nothing persists it. *Verified: code; the setup wizard visibly
  lost its progress on reload.* Fix: real cookie authentication (`AddAuthentication().AddCookie()`,
  `UseAuthentication`/`UseAuthorization`, an `AuthenticationStateProvider`, `[Authorize]` on
  pages) — this also makes 2.1 and 2.5 straightforward. This changes the published NuGet
  package's surface, so it is a minor-version bump for `Lifted.BlazorAuth.Basic`.
  Done when: F5 on any page keeps you signed in, and logout clears the cookie.

- [ ] **1.4 First-run setup completes without SMTP.** With no mail settings the UI says
  "Verification code sent" but the code only goes to the server log
  (`Services/Email/EmailService.cs:52`). *Verified: live.* Fix: when email is unconfigured,
  skip verification during `/setup` (the person at first-run is the owner) and say so;
  never claim an email was sent when it wasn't.
  Done when: a fresh container with zero env vars reaches the dashboard.

- [ ] **1.5 CI builds, tests and releases the app.** The only build workflow
  (`basic-auth-nuget-publish.yml`) packs the NuGet library; nothing builds or releases
  StageZero itself, and `dotnet test` is `continue-on-error` with no test projects.
  *Verified: workflow files.* Fix: a `build.yml` per `Platform-Standards/process/versioning-ci.md`
  (tag-derived versions, pre-release on merge to `main`). Note the repo `CLAUDE.md` still
  describes `BASE_VERSION` + run number — reconcile the two before writing the workflow.
  Done when: a push builds the app and a merge to `main` produces a release.

- [ ] **1.6 Published container image.** No image is built or pushed anywhere, so the
  public has no install path short of cloning and building. Fix: multi-arch
  (`linux/amd64` + `linux/arm64`) image to `ghcr.io/codelifter-platform/stagezero` from the
  release job; README gets a one-line `docker run` and a minimal compose file.
  Done when: `docker run ghcr.io/...` on a Raspberry Pi reaches `/setup`.

## Phase 2 · Security

- [ ] **2.1 Brute-forceable password reset and login.** Reset and verification codes are
  six digits from `new Random()` (`AuthService.cs:172`, `:284`), valid 15 minutes, with no
  attempt limit; login has no lockout or rate limit. *Verified: code.* Fix:
  `RandomNumberGenerator`, longer codes or single-use tokens, max ~5 attempts per code,
  per-account and per-IP throttling on login and reset.
  Done when: a test proves the 6th wrong code invalidates the reset.

- [ ] **2.2 Secrets in logs.** Verification and reset codes are logged in plaintext when
  email is unconfigured (`EmailService.cs:52`, `:108`). *Verified: live log.* Fix: remove;
  1.4 removes the reason they were logged. Done when: no code value appears in any log line.

- [ ] **2.3 Cloudflare DNS token stored in plaintext.** `DnsProvider.ApiToken` is written
  raw to SQLite, while the tunnel token goes through `TunnelTokenProtector`. *Verified:
  old DB copy shows a 40-char raw token, not Data Protection ciphertext.* Fix: protect it
  the same way, with a startup migration that encrypts existing rows.
  Done when: no raw token is readable in `stagezero.db`, and existing installs keep working.

- [ ] **2.4 Public IP shown to anonymous visitors.** `MainLayout.razor:24` renders the
  current-IP chip and the full nav before login — leaking the origin address a Cloudflare
  Tunnel exists to hide. *Verified: live screenshot.* Fix: render the chip and nav only
  when authenticated; auth pages get a bare layout.
  Done when: an anonymous request's HTML contains no IP address.

- [ ] **2.5 Two-factor login.** The admin account guards Cloudflare tokens with a password
  alone. Fix: TOTP (and optionally passkeys) in `Lifted.BlazorAuth.Basic`, after 1.3.
  Done when: TOTP can be enrolled, required at login, and recovered with backup codes.

- [x] **2.6 Vulnerable SQLite native package.** `SQLitePCLRaw.lib.e_sqlite3` 2.1.10 —
  high severity, GHSA-2m69-gcr7-jv3q. *Verified: build warning NU1903.* Fix: bump EF
  Core/SQLite packages or pin a patched `SQLitePCLRaw` version.
  Done when: `dotnet build` shows no NU190x warnings.

- [ ] **2.7 Container runs as root.** `StageZero/Dockerfile` has no `USER`. Fix:
  `USER $APP_UID`, with `/app-data` owned by that user; document the volume-permission
  step for existing installs. Done when: `docker exec … id` is non-root and the DB writes.

- [ ] **2.8 Data Protection configured twice.** `Program.cs:104` (`keys/`) and `:126`
  (`dp-keys/`) both call `AddDataProtection()`; only the second takes effect. Fix: keep one.
  **Check before deleting:** an install whose tunnel token was encrypted under `keys/` would
  lose it — confirm which directory real installs have keys in, and keep that one.
  Done when: one call, one directory, and an upgraded install still decrypts its token.

## Phase 3 · DDNS correctness

- [ ] **3.1 Updates strip the Cloudflare proxy.** `CloudflareDnsService.cs:106-107` sends
  `ttl = 1, proxied = false` on every update, silently turning off orange-cloud on any
  record StageZero touches. *Verified: code.* Fix: read the record's current `proxied` and
  `ttl` and preserve them (or `PATCH` only `content`).
  Done when: a proxied record stays proxied after an IP change.

- [ ] **3.2 Dead IP-change handler.** `IpChangeHandlerService` subscribes to an
  `IpMonitorService` instance in a scope it disposes immediately; the background loop
  raises the event on a different instance every tick. *Verified: real logs show 26
  IP-change events and 0 handler runs.* DDNS works only because `DnsVerificationService`
  re-syncs on every tick. The header's `IpChanged` subscription is dead for the same reason.
  Fix: delete the handler and the event, or move the event to a singleton notifier —
  notifications (6.1) will want the singleton. Done when: no dead subscription remains.

- [ ] **3.3 AAAA auto-update cannot work.** The add dialog offers AAAA with auto-update
  (`AddDnsRecordDialog.razor:65`), but the only IP source is IPv4 (`api.ipify.org`), so an
  IPv4 address would be PUT into an AAAA record and rejected every tick. *(code)* Fix:
  either hide AAAA auto-update now, or do it properly under 6.4.
  Done when: the UI can't create a record that is guaranteed to fail.

- [ ] **3.4 Single IP source, no validation.** One provider, no fallback, and the response
  isn't checked with `IPAddress.TryParse` — a captive-portal HTML page would be recorded as
  an "IP change". *Verified: 11 ipify failures in real logs.* Fix: two or three sources,
  parse-validate, require agreement before acting.
  Done when: a test feeds garbage from one source and no change is recorded.

- [ ] **3.5 No Cloudflare pagination.** `GetZonesAsync` (default 20 per page) and
  `GetDnsRecordsAsync` (default 100) read one page; beyond that records are reported "not
  found" and updates quietly stop. Verification also re-lists the whole zone once per
  record per tick. *(code)* Fix: paginate; list each zone once per tick and match in memory.
  Done when: a zone with 150 records syncs a record on page 2.

- [ ] **3.6 IP history never pruned.** A row every 180 s with no retention — 2,715 rows in
  one month in the old DB. Fix: store only changes plus a "last checked" timestamp, or
  prune beyond N days. Done when: the table stops growing on a stable IP.

## Phase 4 · Hygiene and docs

- [ ] **4.1 No error page.** `Program.cs:434` `UseExceptionHandler("/Error")` points at a
  page that doesn't exist (`/Error` → 404). Done when: a thrown exception in Production
  renders a themed error page.

- [ ] **4.2 Health endpoint.** None exists. Fix: `MapHealthChecks("/healthz")` (DB
  reachable, last IP check recent) and a `HEALTHCHECK` in the Dockerfile.
  Done when: `docker ps` shows `healthy`.

- [ ] **4.3 Real migrations.** Schema changes are hand-written `ALTER TABLE` blocks after
  `EnsureCreated` (`Program.cs:208-419`), each wrapped in a catch that logs and continues.
  Fix: EF Core migrations with a baseline that adopts existing databases.
  Done when: a DB from the last release upgrades cleanly and `Program.cs` has no raw DDL.

- [ ] **4.4 Production log level.** `MinimumLevel.Debug()` is unconditional
  (`Program.cs:58`). Fix: Information in Production, configurable by env var.

- [ ] **4.5 Third-party assets.** `App.razor` loads a personal FontAwesome kit
  (`4b2fedf3b4`, **zero usages** in the codebase) and Roboto from Google Fonts, so every
  public install would call out to both. Fix: delete the kit tag now; fonts are replaced
  under 5.2. Done when: the app renders fully with no outbound asset requests.

- [ ] **4.6 Home page claims.** "Multiple Providers — Support for various DNS providers"
  (Cloudflare only) and "Secure credential storage" (false until 2.3). Replaced by the
  dashboard under 6.2; correct the copy in the meantime.

- [ ] **4.7 LivingSpec is wrong in three places.** Lists Let's Encrypt as working (no such
  code exists); says `StageZero.ReverseProxy` is deleted (folder remains, holding only
  `bin`/`obj`); says `global.json` is missing (it exists). Also fold in the
  versioning-rule conflict from 1.5.

- [ ] **4.8 Repo leftovers.** Untracked-but-present: `StageZero.ReverseProxy/`,
  `StageZero/logs/`, `StageZero/stagezero.db*`. Tracked and shouldn't be:
  `nupkgs/Lifted.BlazorAuth.Basic.0.0.1.nupkg`, `StageZero-Home-Hosting-Guide.docx`, the
  empty `src/Quip/Quip.csproj`. `.env.example` documents `DATABASE_PATH`, which no code
  reads. `CLOUDFLARE_TUNNEL_SETUP.md` and `GITHUB_ACTIONS_SETUP.md` still sit at the root.
  **Decided:** delete the build leftovers and local DB/logs; move the `.docx` into
  `..Documentation/`.

- [x] **4.9 Compiler warnings and build props.** Six warnings today (CS8602 in
  `Login.razor:150`, CS8604 ×2 in `EmailService.cs`, CS0105 duplicate `using` in
  `Program.cs:18`). The harness requires warnings-as-errors and central package versions:
  add `Directory.Build.props` and `Directory.Packages.props`.
  Done when: the build is warning-free with `TreatWarningsAsErrors`.

## Phase 5 · Design system rebuild

Read `Platform-Standards/design/design-system.md`, then `Platform-Design/readme.md`,
`tokens/`, and each component's `.prompt.md` **before any markup**. If a component, token or
accent is missing, stop and ask for it to be added.

- [ ] **5.1 StageZero has no locked accent.** It isn't in the harness accent table, and the
  app currently shows stock MudBlazor purple. **Decided:** lock `#34d399` (inner gradient
  `#0e2b22`, deep mark `#15795b`, from `Assets/svg/`) and derive the light-mode accent.
- [ ] **5.2 Token-driven `MudTheme`** with deliberate Dark and Light palettes ported from
  Platform-Design; Inter + JetBrains Mono bundled in `wwwroot` (no CDN).
- [ ] **5.3 Remove raw colours.** Inline `white`/`black` in `MainLayout.razor:29` and
  `:168`, `white-text` on Home, and `#594AE2` in the email templates.
- [ ] **5.4 Persist the theme choice.** Dark mode resets to light on reload. *Verified:
  live.* Persist per user (settings table or cookie) and apply before first paint.
- [ ] **5.5 Layout defects.** The Home nav icon is clipped under the app bar
  (`DrawerClipMode.Never`); form buttons enable only on blur rather than as you type; the
  icon-only drawer has no labels.

## Phase 6 · Killer-app features

Ordered by value for a home-infrastructure user.

- [ ] **6.1 Notifications** — IP changed, DNS update failed, tunnel down — via ntfy,
  Discord and generic webhook. The app has none today.
- [ ] **6.2 Real dashboard** replacing the marketing Home page: current IP, tunnel
  connector status, per-record sync state, last change, recent errors.
- [ ] **6.3 Service health per tunnel route** — is `jellyfin.example.com` up, and how fast.
  Turns StageZero from a DDNS updater into a control panel.
- [ ] **6.4 IPv6 / AAAA** with separate v4 and v6 detection (builds on 3.3 and 3.4).
- [ ] **6.5 One-click Cloudflare Access on a route** (email / one-time code) to protect
  exposed home services.
- [ ] **6.6 Import existing routes when adopting a tunnel.** Today the first route added
  replaces whatever ingress the adopted tunnel had (the UI warns, but doesn't import).
- [ ] **6.7 Multiple zones per tunnel and wildcard hostnames.** `TunnelConfig` holds a
  single zone.
- [ ] **6.8 Audit log of every DNS and tunnel change** — old value, new value, reason.
- [ ] **6.9 Config backup / export and restore.**
- [ ] **6.10 Cloudflare-native positioning.** **Decided:** no second provider. Remove the
  provider-type plumbing that implies otherwise, and say "built for Cloudflare" plainly in
  the UI and README.

## Phase 7 · Marketing screenshots

Capture only after Phase 5. Use documentation addresses (`203.0.113.x`) and example
domains, never real ones.

- [ ] **7.1 Tunnel Routes table** — populated (`plex.`, `home.`, `git.` →
  `192.168.x.x:port`) with enabled chips. The hero shot.
- [ ] **7.2 Tunnel setup, connector-token step** — the `cloudflared` command ready to copy.
- [ ] **7.3 IP Monitor** — large current IP, "Check Now", history with a real change in it.
- [ ] **7.4 DNS Configuration** — records with sync status and last-updated times.
- [ ] **7.5 Dark and light side by side** on the routes page.
- [ ] **7.6 First-run setup wizard** (after 1.4).
- [ ] **7.7 The new dashboard** (after 6.2).

---

## Owner decisions (2026-09-21)

1. **Accent:** lock `#34d399` — the emerald the brand assets already use — in
   `Platform-Standards/design/app-accents.md`, with a light-mode accent derived by the
   documented rule (5.1).
2. **Versioning:** adopt the harness tag scheme (`next-version.sh`, `-pre` on merge,
   RELEASE MINOR / MAJOR buttons) for the app and the NuGet package; the repo `CLAUDE.md`
   is updated to match (1.5).
3. **Deletions (4.8):** delete `StageZero.ReverseProxy/`, `nupkgs/`, `src/Quip`, and the
   local `StageZero/stagezero.db*` and `StageZero/logs/`. Keep the hosting guide, moved
   into `..Documentation/`.
4. **DNS providers (6.10):** Cloudflare-native. No second provider; the multi-provider claim
   goes.

## Work order

Items are ticked in dependency order, not numeric order. Foundations go first so later
items don't build on something about to change: 1.1 → 4.9 → 2.6 → 1.2 (test project) →
4.3 (EF migrations, so every later schema change is a migration) → the rest of Phases 1–4
→ Phase 5 → Phase 6 (new UI is built on the Phase 5 theme) → Phase 7.
