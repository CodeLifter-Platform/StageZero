# ═══════════════════════════════════════════════════════════════
# StageZero Docker Run Script (PowerShell)
# ═══════════════════════════════════════════════════════════════
# This script sets up platform-specific data directories and runs
# Docker Compose with the correct volume mounts.
#
# Usage:
#   .\docker-run.ps1 [up|down|logs|restart|build]
#
# The application data (database, logs) will be stored in:
#   Windows: %APPDATA%\StageZero\
# ═══════════════════════════════════════════════════════════════

param(
    [Parameter(Position=0)]
    [ValidateSet('up', 'down', 'logs', 'restart', 'build')]
    [string]$Command = 'up'
)

# Get Windows AppData directory
$StageZeroDataDir = Join-Path $env:APPDATA "StageZero"

# Create data directory if it doesn't exist
if (-not (Test-Path $StageZeroDataDir)) {
    New-Item -ItemType Directory -Path $StageZeroDataDir -Force | Out-Null
}

Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "StageZero Docker Compose" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "Platform: Windows"
Write-Host "Data Directory: $StageZeroDataDir"
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Set environment variable for docker-compose
$env:STAGEZERO_DATA_DIR = $StageZeroDataDir

switch ($Command) {
    'up' {
        Write-Host "Starting StageZero..." -ForegroundColor Green
        docker-compose -f debug.docker-compose.yml up --build -d
        Write-Host ""
        Write-Host "✅ StageZero is running!" -ForegroundColor Green
        Write-Host "   Web UI: http://localhost:5000"
        Write-Host "   Data:   $StageZeroDataDir"
        Write-Host ""
        Write-Host "To view logs: .\docker-run.ps1 logs"
        Write-Host "To stop:      .\docker-run.ps1 down"
    }
    'down' {
        Write-Host "Stopping StageZero..." -ForegroundColor Yellow
        docker-compose -f debug.docker-compose.yml down
        Write-Host "✅ StageZero stopped" -ForegroundColor Green
    }
    'logs' {
        docker-compose -f debug.docker-compose.yml logs -f stagezero
    }
    'restart' {
        Write-Host "Restarting StageZero..." -ForegroundColor Yellow
        docker-compose -f debug.docker-compose.yml restart stagezero
        Write-Host "✅ StageZero restarted" -ForegroundColor Green
    }
    'build' {
        Write-Host "Building StageZero..." -ForegroundColor Yellow
        docker-compose -f debug.docker-compose.yml build
        Write-Host "✅ Build complete" -ForegroundColor Green
    }
}

