# StageZero

**Self-hosting control panel for Cloudflare DNS and Cloudflare Tunnels**

A Blazor Server application that keeps your domains pointed at the right IP
addresses and publishes your local services to the internet through a Cloudflare
Tunnel — no port forwarding, no certificate management, no public IP required.

Three features, one Cloudflare API token:

- **Dynamic DNS** — monitors your public IP and updates Cloudflare DNS records when it changes.
- **Tunnel Routes** — maps public hostnames to services on your network by pushing
  ingress rules and proxied CNAMEs to Cloudflare. See
  [..Documentation/CLOUDFLARE_TUNNEL_SETUP.md](..Documentation/CLOUDFLARE_TUNNEL_SETUP.md).
- **Access (Zero Trust)** — puts an authentication policy in front of every hostname it
  publishes, so a tunnel does not mean an open door. See
  [..Documentation/CLOUDFLARE_ACCESS_SETUP.md](..Documentation/CLOUDFLARE_ACCESS_SETUP.md).

## Tech Stack

- .NET 10.0 / Blazor Server (InteractiveServer mode)
- MudBlazor UI Framework
- SQLite with Entity Framework Core
- Basic Username/Password Authentication
- Serilog for Structured Logging

## Getting Started

### Prerequisites

- .NET 10.0 SDK
- Docker & Docker Compose (for containerized development)

### Local Development

1. **Copy environment file:**
   ```bash
   cp .env.example .env
   ```

2. **Run the application:**
   ```bash
   cd StageZero
   dotnet run
   ```

3. **Access the app:** https://localhost:5001

### Docker Development

1. **Copy environment file:**
   ```bash
   cp .env.example .env
   ```

2. **Start everything:**
   ```bash
   docker-compose -f debug.docker-compose.yml up
   ```

3. **Access the app:** http://localhost:5000

### Production (behind a Cloudflare Tunnel)

```bash
./docker-run.sh up prod        # macOS / Linux
.\docker-run.ps1 up prod       # Windows
```

Runs the release image on plain HTTP at `127.0.0.1:5100`; Cloudflare terminates
TLS at the edge. Follow [..Documentation/CLOUDFLARE_TUNNEL_SETUP.md](..Documentation/CLOUDFLARE_TUNNEL_SETUP.md)
to install the connector and publish it.

## Project Structure

```
StageZero/
├── StageZero/
│   ├── Application/           # UI Layer
│   │   ├── Areas/            # Feature areas
│   │   │   ├── DnsConfig/        # DNS providers + records
│   │   │   ├── IpMonitor/        # Public IP history
│   │   │   └── TunnelManagement/ # Tunnel routes, Access, setup wizard
│   │   ├── Components/       # Shared components
│   │   └── Layout/           # MainLayout, AppVM
│   ├── Data/                 # DbContext
│   ├── DataAdapters/         # Data access (Readers/Writers)
│   ├── Models/               # Domain entities
│   ├── Services/             # Business logic
│   │   ├── Access/               # Cloudflare Access apps, policies, service tokens
│   │   ├── Dns/                  # Cloudflare DNS + DDNS updates
│   │   ├── IpMonitoring/         # Public IP polling
│   │   └── Tunnel/               # Cloudflare Tunnel API + route sync
│   └── wwwroot/              # Static assets
├── StageZero.Tests/           # Unit tests (xUnit)
├── debug.docker-compose.yml   # Hot-reload development
├── prod.docker-compose.yml    # Release build + optional cloudflared sidecar
├── .env.example
└── StageZero.sln
```

## Architecture

This project follows **MVVM** architecture:

- **Views** (Razor components) - Zero logic, bind to ViewModels
- **ViewModels** - UI state translation only
- **Services** - All business logic
- **DataAdapters** - Focused data access (Reader/Writer pattern)

## Configuration

### Environment Variables

The application uses a `.env` file for configuration. Copy `.env.example` to `.env` and configure as needed:

```bash
cp .env.example .env
```

**Email Configuration (Optional):**

To enable email verification for new user setup, configure SMTP settings in `.env`:

```bash
# Uncomment and configure these to enable email sending:
Email__SmtpHost=smtp.gmail.com
Email__SmtpPort=587
Email__SmtpUsername=your-email@gmail.com
Email__SmtpPassword=your-app-password
Email__FromEmail=your-email@gmail.com
Email__FromName=StageZero
```

**Gmail Setup:**
1. Enable 2-factor authentication on your Google account
2. Generate an App Password: https://myaccount.google.com/apppasswords
3. Use the App Password (not your regular password) in `Email__SmtpPassword`

**Note:** If SMTP is not configured, verification codes will be logged to the console during development.

### DNS Provider Control

Each DNS provider (Cloudflare, etc.) can be individually enabled or disabled for updates:

- **When adding a provider**: Toggle "Enable DNS Updates" in the add provider dialog
- **After adding**: Use the toggle switch in the provider card header to enable/disable updates

When a provider is disabled:
- ✅ IP monitoring continues normally
- ✅ DNS records are still visible in the UI
- ❌ NO automatic DNS updates will be made for that provider

This is useful for:
- Testing without affecting production DNS
- Temporarily pausing updates for specific providers
- Development without valid DNS credentials

### Cloudflare API Token

One token drives all three features. Create it at **My Profile → API Tokens** with:

| Scope | Permission | Needed for |
|---|---|---|
| Account | Cloudflare Tunnel → Edit | Tunnel routes |
| Zone | DNS → Edit | Dynamic DNS and tunnel CNAMEs |
| Zone | Zone → Read | Listing zones during setup |
| Account | Access: Apps → Edit | Access, any mode except `none` |
| Account | Access: Service Tokens → Edit | Access, `service_token` and `both` modes |
| Account | Access: Organizations, Identity Providers, and Groups → Read | Listing identity providers |

The three Access permissions are **account-scoped**, unlike the zone-scoped DNS
permissions — a token that writes DNS records fine may still have no Access rights
at all. StageZero checks before provisioning and names any that are missing. Leave
them off entirely if you only want DNS and tunnels; the rest of the app is
unaffected.

The tunnel token is encrypted with ASP.NET Data Protection before storage. The
keyring lives in the app data directory (`/app-data/dp-keys` in Docker), which
must be on a persistent volume — otherwise the token can't be decrypted after a
restart.

### Cloudflare Access

Every hostname StageZero publishes is protected by Access by default. Each route
picks one of four modes:

| Mode | Who gets in |
|---|---|
| `identity` | People signing in through an identity provider, matched by email or email domain |
| `service_token` | Machines presenting a Cloudflare service token |
| `both` | Either |
| `none` | Everyone — no Access application at all |

New hostnames default to `identity`, allowing the owner email (the
`Access.OwnerEmail` setting, or the first account created during setup). Choosing
`none` is explicit and the UI marks those routes as public.

When StageZero mints a service token, **Cloudflare returns the client secret only
once**. It is shown in a dialog that must be acknowledged before it closes, and is
never logged, stored, or written to disk. Implement `IAccessSecretSink` to also
route it into a vault.

Full details — modes, permissions, idempotency, teardown rules and the rollback
behaviour when Access setup fails — are in
[..Documentation/CLOUDFLARE_ACCESS_SETUP.md](..Documentation/CLOUDFLARE_ACCESS_SETUP.md).

### VS Code Debugging

The project includes VS Code launch configuration with `.env` file support. To debug:

1. Open the project in VS Code
2. Press `F5` or use the "Run and Debug" panel
3. Select ".NET Core Launch (web)"

The `.env` file will be automatically loaded when debugging.

### Layout and theme
- Dense header with the current public IP and the light/dark toggle
- Mini navigation drawer
- 80% body width, centered, scrollable content
- CodeLifter design system: dark and light themes, StageZero teal accent, Inter + JetBrains
  Mono bundled with the app. Colors come only from `Application/Theme/StageZeroTheme.cs`.

### Database
SQLite, created automatically as `stagezero.db` in the app data directory (`/app-data` in
a container, the per-user config directory otherwise).

## Documentation

Everything beyond this README lives in [`..Documentation/`](..Documentation/) (a hidden
folder — `ls -a`):

| Doc | For |
|---|---|
| [LivingSpec.md](..Documentation/LivingSpec.md) | What StageZero does today, how it is built, and its known gaps |
| [OnboardWeb.md](..Documentation/OnboardWeb.md) | Build, run, and test on a machine with the .NET SDK |
| [OnboardDocker.md](..Documentation/OnboardDocker.md) | Run it in Docker |
| [CLOUDFLARE_TUNNEL_SETUP.md](..Documentation/CLOUDFLARE_TUNNEL_SETUP.md) | Publishing hostnames through a Cloudflare Tunnel |
| [CLOUDFLARE_ACCESS_SETUP.md](..Documentation/CLOUDFLARE_ACCESS_SETUP.md) | Access modes, permissions, service tokens |
| [DOCKER_SETUP.md](..Documentation/DOCKER_SETUP.md) | Compose files, VS Code debugging, data persistence |
| [NUGET_PUBLISHING.md](..Documentation/NUGET_PUBLISHING.md) | How `Lifted.BlazorAuth.Basic` is versioned and published |

## Tests

```bash
dotnet test StageZero.Tests/StageZero.Tests.csproj
```

Cloudflare is faked at the service interfaces, so the tests make no network calls.

## License

[MIT](LICENSE) © CodeLifter LLC

## Downloads

| Platform | Formats | Get it |
|---|---|---|
| **Container** | `Dockerfile` (`debug` / `release` stages) | [build from source](..Documentation/OnboardDocker.md) |
| **Any .NET 10 host** | `dotnet run` / published output | [Releases](https://github.com/CodeLifter-Platform/StageZero/releases) |
| **NuGet** | `Lifted.BlazorAuth.Basic` | [nuget.org](https://www.nuget.org/packages/Lifted.BlazorAuth.Basic) |

> StageZero is a server application — there is nothing to install on a desktop. The repo also
> publishes the `Lifted.BlazorAuth.Basic` package; see `..Documentation/NUGET_PUBLISHING.md`.

## Release history

Versions come from `BASE_VERSION` + the CI run number, with `RELEASE_LEVEL` adding the
prerelease suffix. Releases happen on push to `main` — there are no hand-pushed tags, and
reintroducing a `tags:` trigger is exactly what the platform versioning contract rules out.

| Version | Date | Package | Notes |
|---|---|---|---|
