# ═══════════════════════════════════════════════════════════════
# DEBUG STAGE
# ═══════════════════════════════════════════════════════════════
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS debug
WORKDIR /src

# Copy solution file and all project files
COPY StageZero.sln ./
COPY StageZero/*.csproj ./StageZero/
COPY StageZero.ReverseProxy/*.csproj ./StageZero.ReverseProxy/
COPY Lifted.BlazorAuth.Basic/*.csproj ./Lifted.BlazorAuth.Basic/

# Restore dependencies
RUN dotnet restore StageZero/StageZero.csproj

# Copy all source files
COPY StageZero/ ./StageZero/
COPY StageZero.ReverseProxy/ ./StageZero.ReverseProxy/
COPY Lifted.BlazorAuth.Basic/ ./Lifted.BlazorAuth.Basic/

# Set working directory to the main project
WORKDIR /src/StageZero

# Enable hot reload for development
ENV DOTNET_USE_POLLING_FILE_WATCHER=true
ENV ASPNETCORE_ENVIRONMENT=Development

EXPOSE 80
EXPOSE 443

ENTRYPOINT ["dotnet", "watch", "run", "--launch-profile", "Docker"]

# ═══════════════════════════════════════════════════════════════
# BUILD STAGE
# ═══════════════════════════════════════════════════════════════
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution file and all project files
COPY StageZero.sln ./
COPY StageZero/*.csproj ./StageZero/
COPY StageZero.ReverseProxy/*.csproj ./StageZero.ReverseProxy/
COPY Lifted.BlazorAuth.Basic/*.csproj ./Lifted.BlazorAuth.Basic/

# Restore dependencies
RUN dotnet restore StageZero/StageZero.csproj

# Copy all source files
COPY StageZero/ ./StageZero/
COPY StageZero.ReverseProxy/ ./StageZero.ReverseProxy/
COPY Lifted.BlazorAuth.Basic/ ./Lifted.BlazorAuth.Basic/

# Build and publish
RUN dotnet publish StageZero/StageZero.csproj -c Release -o /app/publish

# ═══════════════════════════════════════════════════════════════
# RELEASE STAGE
# ═══════════════════════════════════════════════════════════════
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS release
WORKDIR /app
EXPOSE 80
EXPOSE 443

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "StageZero.dll"]

