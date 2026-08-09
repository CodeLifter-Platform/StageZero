# StageZero — Blazor Server dynamic DNS.
#
# Build context is the REPO ROOT, not this directory, because the web project references
# ../StageZero.ReverseProxy and ../Lifted.BlazorAuth.Basic:
#
#   docker build -f StageZero/Dockerfile -t stagezero:local .
#
# Both compose files already expect that (context: ., dockerfile: StageZero/Dockerfile)
# and select a stage with `target:`, so the `debug` and `release` stage names below are a
# contract with them — renaming one breaks docker-compose.
#
# The app resolves its own data directory: DataPathService detects a container and uses
# /app-data, so the SQLite database and logs land on the mounted volume rather than inside
# the image. Nothing here needs to pass a connection string.

# ── Base runtime ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

# Kestrel listens on 8080 by default in these images. The compose files override
# ASPNETCORE_URLS to bind 443 with a mounted dev certificate; both work.
EXPOSE 8080
EXPOSE 443

# The volume mount point. Created here so a plain `docker run` without a volume still has
# somewhere writable to put the database instead of failing at startup.
RUN mkdir -p /app-data
VOLUME /app-data

# ── Build ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Restore against the project files alone first, so the layer cache survives source edits.
# No Directory.Build.props / Directory.Packages.props / global.json in this repo yet —
# it is one of the repos the conformance checker flags for missing .NET pins. Add them
# to this COPY when they land, or the restore layer will silently stop matching the build.
COPY ["StageZero/StageZero.csproj", "StageZero/"]
COPY ["StageZero.ReverseProxy/StageZero.ReverseProxy.csproj", "StageZero.ReverseProxy/"]
COPY ["Lifted.BlazorAuth.Basic/Lifted.BlazorAuth.Basic.csproj", "Lifted.BlazorAuth.Basic/"]
RUN dotnet restore "StageZero/StageZero.csproj"

COPY . .
RUN dotnet build "StageZero/StageZero.csproj" -c "$BUILD_CONFIGURATION" -o /app/build --no-restore

# ── Publish ─────────────────────────────────────────────────────────
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "StageZero/StageZero.csproj" -c "$BUILD_CONFIGURATION" -o /app/publish /p:UseAppHost=false

# ── debug: what debug.docker-compose.yml targets ────────────────────
FROM base AS debug
ENV ASPNETCORE_ENVIRONMENT=Development
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "StageZero.dll"]

# ── release: what beta.docker-compose.yml targets, and the default ──
FROM base AS release
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "StageZero.dll"]
