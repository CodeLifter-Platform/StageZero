# ═══════════════════════════════════════════════════════════════
# StageZero Docker Run Script (PowerShell)
# ═══════════════════════════════════════════════════════════════
# Wraps `docker compose` and, for the release instance, puts the data where the
# host-run app would keep it: %APPDATA%\StageZero\
#
# Usage:
#   .\docker-run.ps1 [up|down|logs|restart|build] [release|debug]
#
# release (default) — docker-compose.yml, the instance you keep running, on
#                     http://localhost:5100
# debug             — docker-compose.debug.yml, hot reload for the API and the web
#                     app, on http://localhost:5173 (web) and :5200 (API); its data
#                     stays in .\.volumes\stagezero-debug, apart from the real instance
# ═══════════════════════════════════════════════════════════════

param(
    [Parameter(Position=0)]
    [ValidateSet('up', 'down', 'logs', 'restart', 'build')]
    [string]$Command = 'up',

    [Parameter(Position=1)]
    [ValidateSet('release', 'debug')]
    [string]$Target = 'release'
)

switch ($Target) {
    'release' {
        $ComposeFile = 'docker-compose.yml'
        $Service     = 'stagezero'
        $AppUrl      = 'http://localhost:5100'
        if (-not $env:STAGEZERO_DATA_DIR) {
            $env:STAGEZERO_DATA_DIR = Join-Path $env:APPDATA "StageZero"
        }
        if (-not (Test-Path $env:STAGEZERO_DATA_DIR)) {
            New-Item -ItemType Directory -Path $env:STAGEZERO_DATA_DIR -Force | Out-Null
        }
        $DataDir = $env:STAGEZERO_DATA_DIR
    }
    'debug' {
        $ComposeFile = 'docker-compose.debug.yml'
        $Service     = 'stagezero'
        $AppUrl      = 'http://localhost:5173 (web app) / http://localhost:5200 (API)'
        $DataDir     = if ($env:STAGEZERO_DEBUG_DATA_DIR) { $env:STAGEZERO_DEBUG_DATA_DIR } else { '.\.volumes\stagezero-debug' }
    }
}

Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "StageZero Docker Compose" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "Target: $Target ($ComposeFile)"
Write-Host "Data:   $DataDir"
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

switch ($Command) {
    'up' {
        Write-Host "Starting StageZero..." -ForegroundColor Green
        docker compose -f $ComposeFile up --build -d
        Write-Host ""
        Write-Host "✅ StageZero is running!" -ForegroundColor Green
        Write-Host "   Open:   $AppUrl"
        Write-Host "   Data:   $DataDir"
        Write-Host ""
        Write-Host "To view logs: .\docker-run.ps1 logs $Target"
        Write-Host "To stop:      .\docker-run.ps1 down $Target"
    }
    'down' {
        Write-Host "Stopping StageZero..." -ForegroundColor Yellow
        docker compose -f $ComposeFile down
        Write-Host "✅ StageZero stopped" -ForegroundColor Green
    }
    'logs' {
        docker compose -f $ComposeFile logs -f
    }
    'restart' {
        Write-Host "Restarting StageZero..." -ForegroundColor Yellow
        docker compose -f $ComposeFile restart $Service
        Write-Host "✅ StageZero restarted" -ForegroundColor Green
    }
    'build' {
        Write-Host "Building StageZero..." -ForegroundColor Yellow
        docker compose -f $ComposeFile build
        Write-Host "✅ Build complete" -ForegroundColor Green
    }
}
