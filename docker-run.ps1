# ═══════════════════════════════════════════════════════════════
# StageZero Docker Run Script (PowerShell)
# ═══════════════════════════════════════════════════════════════
# This script sets up platform-specific data directories and runs
# Docker Compose with the correct volume mounts.
#
# Usage:
#   .\docker-run.ps1 [up|down|logs|restart|build] [debug|prod]
#
# debug (default) — hot-reload container on https://localhost:5000
# prod            — release build on http://127.0.0.1:5100, meant to sit behind
#                   a Cloudflare Tunnel. See CLOUDFLARE_TUNNEL_SETUP.md.
#
# The application data (database, logs, data-protection keys) will be stored in:
#   Windows: %APPDATA%\StageZero\
# ═══════════════════════════════════════════════════════════════

param(
    [Parameter(Position=0)]
    [ValidateSet('up', 'down', 'logs', 'restart', 'build')]
    [string]$Command = 'up',

    [Parameter(Position=1)]
    [ValidateSet('debug', 'prod')]
    [string]$Target = 'debug'
)

# Get Windows AppData directory
$StageZeroDataDir = Join-Path $env:APPDATA "StageZero"

# Create data directory if it doesn't exist
if (-not (Test-Path $StageZeroDataDir)) {
    New-Item -ItemType Directory -Path $StageZeroDataDir -Force | Out-Null
}

switch ($Target) {
    'debug' {
        $ComposeFile = 'debug.docker-compose.yml'
        $Service     = 'debug-stagezero'
        $AppUrl      = 'https://localhost:5000'
    }
    'prod' {
        $ComposeFile = 'prod.docker-compose.yml'
        $Service     = 'prod-stagezero'
        $AppUrl      = 'http://127.0.0.1:5100'
    }
}

Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "StageZero Docker Compose" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "Platform: Windows"
Write-Host "Target:   $Target ($ComposeFile)"
Write-Host "Data Directory: $StageZeroDataDir"
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Set environment variable for docker-compose
$env:STAGEZERO_DATA_DIR = $StageZeroDataDir

switch ($Command) {
    'up' {
        Write-Host "Starting StageZero..." -ForegroundColor Green
        docker-compose -f $ComposeFile up --build -d
        Write-Host ""
        Write-Host "✅ StageZero is running!" -ForegroundColor Green
        Write-Host "   Web UI: $AppUrl"
        Write-Host "   Data:   $StageZeroDataDir"
        Write-Host ""
        Write-Host "To view logs: .\docker-run.ps1 logs $Target"
        Write-Host "To stop:      .\docker-run.ps1 down $Target"
    }
    'down' {
        Write-Host "Stopping StageZero..." -ForegroundColor Yellow
        docker-compose -f $ComposeFile down
        Write-Host "✅ StageZero stopped" -ForegroundColor Green
    }
    'logs' {
        docker-compose -f $ComposeFile logs -f $Service
    }
    'restart' {
        Write-Host "Restarting StageZero..." -ForegroundColor Yellow
        docker-compose -f $ComposeFile restart $Service
        Write-Host "✅ StageZero restarted" -ForegroundColor Green
    }
    'build' {
        Write-Host "Building StageZero..." -ForegroundColor Yellow
        docker-compose -f $ComposeFile build
        Write-Host "✅ Build complete" -ForegroundColor Green
    }
}
