# StageZero — dynamic DNS and Cloudflare Tunnel manager: an ASP.NET Core Web API that also
# serves the React app built from web/.
#
# Build context is the REPO ROOT, not this directory, because the API project references
# ../Lifted.BlazorAuth.Basic and the web app lives in ../web:
#
#   docker build -f StageZero/Dockerfile -t stagezero:local .
#
# Two stages are a contract with the compose files at the repo root — renaming one breaks
# them:
#
#   release  what docker-compose.yml runs: the published app, non-root, HEALTHCHECK.
#   debug    what docker-compose.debug.yml runs: `dotnet watch` over the mounted source,
#            with the VS Code debugger installed.
#
# Both listen on plain HTTP on 8080. The app resolves its own data directory:
# DataPathService detects a container and uses /app-data, so the SQLite database, logs and
# data-protection keys land on the mounted volume rather than inside the image. Nothing here
# needs to pass a connection string.

# ── Base runtime ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

# Kestrel listens on 8080 by default in these images; the compose files publish it.
EXPOSE 8080

# The volume mount point. Created here so a plain `docker run` without a volume still has
# somewhere writable to put the database instead of failing at startup. Owned by the
# unprivileged user the release stage runs as ($APP_UID, 1654 in the .NET images): a named
# volume created against this image inherits that ownership; a bind mount or a volume from
# an older (root) install needs `chown -R 1654 <dir>` once (see OnboardDocker.md).
RUN mkdir -p /app-data && chown $APP_UID /app-data
VOLUME /app-data

# ── Web app ─────────────────────────────────────────────────────────
# Builds web/ (Vite + React) into /web/wwwroot. Lint and tests run in CI, not here.
FROM node:22-alpine AS web
WORKDIR /src/web
COPY ["web/package.json", "web/package-lock.json", "./"]
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npx tsc -b && npx vite build --outDir /web/wwwroot --emptyOutDir

# ── Build ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Restore against the project files alone first, so the layer cache survives source edits.
# The solution-wide props carry the package versions and build settings, so they belong in
# this layer too — without Directory.Packages.props the restore has no versions to resolve.
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["StageZero/StageZero.csproj", "StageZero/"]
COPY ["Lifted.BlazorAuth.Basic/Lifted.BlazorAuth.Basic.csproj", "Lifted.BlazorAuth.Basic/"]
RUN dotnet restore "StageZero/StageZero.csproj"

COPY . .
RUN dotnet build "StageZero/StageZero.csproj" -c "$BUILD_CONFIGURATION" -o /app/build --no-restore

# ── Publish ─────────────────────────────────────────────────────────
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "StageZero/StageZero.csproj" -c "$BUILD_CONFIGURATION" -o /app/publish /p:UseAppHost=false
COPY --from=web /web/wwwroot /app/publish/wwwroot

# ── debug: what docker-compose.debug.yml runs ───────────────────────
# A hot-reload dev environment for the API, not the published app with a different env var.
# It runs `dotnet watch` off the SDK image; the compose file mounts the source over /src so
# edits on the host take effect without a rebuild. The web app is built once into the image
# and served from /web/wwwroot — deliberately outside /src, where the bind mount would hide
# it; the compose file's `web` service runs the Vite dev server for UI hot reload.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS debug
WORKDIR /src

# The VS Code debugger, so "StageZero container: attach" in .vscode/launch.json can pipe
# into this container through `docker exec`.
RUN curl -sSL https://aka.ms/getvsdbgsh | bash /dev/stdin -v latest -l /vsdbg

COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["StageZero/StageZero.csproj", "StageZero/"]
COPY ["Lifted.BlazorAuth.Basic/Lifted.BlazorAuth.Basic.csproj", "Lifted.BlazorAuth.Basic/"]
RUN dotnet restore "StageZero/StageZero.csproj"
COPY . .
COPY --from=web /web/wwwroot /web/wwwroot

RUN mkdir -p /app-data
VOLUME /app-data
EXPOSE 8080

WORKDIR /src/StageZero
# Polling is required: inotify does not fire for edits made on the host and seen through a
# bind mount. No launch profile: launchSettings.json binds localhost, unreachable from the
# host; the URL comes from the environment instead.
ENV DOTNET_USE_POLLING_FILE_WATCHER=true \
    ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_WEBROOT=/web/wwwroot
ENTRYPOINT ["dotnet", "watch", "run", "--no-launch-profile", "--non-interactive"]

# ── release: what docker-compose.yml runs, and the default ──────────
FROM base AS release
COPY --from=publish /app/publish .

# Not root. Kestrel's 8080 needs no privilege.
USER $APP_UID

# The app probes its own /healthz (the image has no curl). Degraded — the public IP not
# confirmed lately — still counts as healthy: restarting wouldn't fix an upstream outage.
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD ["dotnet", "StageZero.dll", "healthcheck"]

ENTRYPOINT ["dotnet", "StageZero.dll"]
