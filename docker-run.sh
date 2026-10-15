#!/bin/bash

# ═══════════════════════════════════════════════════════════════
# StageZero Docker Run Script
# ═══════════════════════════════════════════════════════════════
# Wraps `docker compose` and, for the release instance, puts the data where the
# host-run app would keep it:
#   macOS: ~/Library/Application Support/StageZero/
#   Linux: ~/.config/stagezero/
#
# Usage:
#   ./docker-run.sh [up|down|logs|restart|build] [release|debug]
#
# release (default) — docker-compose.yml, the instance you keep running, on
#                     http://localhost:5100
# debug             — docker-compose.debug.yml, hot reload for the API and the web
#                     app, on http://localhost:5173 (web) and :5200 (API); its data
#                     stays in ./.volumes/stagezero-debug, apart from the real instance
# ═══════════════════════════════════════════════════════════════

set -e

COMMAND="${1:-up}"
TARGET="${2:-release}"

case "$TARGET" in
    release)
        COMPOSE_FILE="docker-compose.yml"
        SERVICE="stagezero"
        APP_URL="http://localhost:5100"
        if [[ -z "$STAGEZERO_DATA_DIR" ]]; then
            if [[ "$OSTYPE" == "darwin"* ]]; then
                STAGEZERO_DATA_DIR="$HOME/Library/Application Support/StageZero"
            elif [[ "$OSTYPE" == "linux-gnu"* ]]; then
                STAGEZERO_DATA_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/stagezero"
            else
                echo "Unsupported platform: $OSTYPE — using ./.volumes/stagezero-data"
                STAGEZERO_DATA_DIR="./.volumes/stagezero-data"
            fi
        fi
        mkdir -p "$STAGEZERO_DATA_DIR"
        export STAGEZERO_DATA_DIR
        DATA_DIR="$STAGEZERO_DATA_DIR"
        ;;
    debug)
        COMPOSE_FILE="docker-compose.debug.yml"
        SERVICE="stagezero"
        APP_URL="http://localhost:5173 (web app) · http://localhost:5200 (API)"
        DATA_DIR="${STAGEZERO_DEBUG_DATA_DIR:-./.volumes/stagezero-debug}"
        ;;
    *)
        echo "Unknown target: $TARGET (expected 'release' or 'debug')"
        exit 1
        ;;
esac

compose() {
    docker compose -f "$COMPOSE_FILE" "$@"
}

echo "═══════════════════════════════════════════════════════════════"
echo "StageZero Docker Compose"
echo "═══════════════════════════════════════════════════════════════"
echo "Target: $TARGET ($COMPOSE_FILE)"
echo "Data:   $DATA_DIR"
echo "═══════════════════════════════════════════════════════════════"
echo ""

case "$COMMAND" in
    up)
        echo "Starting StageZero..."
        compose up --build -d
        echo ""
        echo "✅ StageZero is running!"
        echo "   Open:   $APP_URL"
        echo "   Data:   $DATA_DIR"
        echo ""
        echo "To view logs: ./docker-run.sh logs $TARGET"
        echo "To stop:      ./docker-run.sh down $TARGET"
        ;;
    down)
        echo "Stopping StageZero..."
        compose down
        echo "✅ StageZero stopped"
        ;;
    logs)
        compose logs -f
        ;;
    restart)
        echo "Restarting StageZero..."
        compose restart "$SERVICE"
        echo "✅ StageZero restarted"
        ;;
    build)
        echo "Building StageZero..."
        compose build
        echo "✅ Build complete"
        ;;
    *)
        echo "Usage: $0 [up|down|logs|restart|build] [release|debug]"
        echo ""
        echo "Commands:"
        echo "  up       - Start StageZero (default)"
        echo "  down     - Stop StageZero"
        echo "  logs     - Follow the logs"
        echo "  restart  - Restart the app container"
        echo "  build    - Rebuild the image"
        echo ""
        echo "Targets:"
        echo "  release  - The instance you keep running (default)"
        echo "  debug    - Hot-reload API + web app, debugger attachable"
        exit 1
        ;;
esac
