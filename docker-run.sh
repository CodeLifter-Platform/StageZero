#!/bin/bash

# ═══════════════════════════════════════════════════════════════
# StageZero Docker Run Script
# ═══════════════════════════════════════════════════════════════
# This script sets up platform-specific data directories and runs
# Docker Compose with the correct volume mounts.
#
# Usage:
#   ./docker-run.sh [up|down|logs|restart|build] [debug|prod]
#
# debug (default) — hot-reload container on https://localhost:5000
# prod            — release build on http://127.0.0.1:5100, meant to sit behind
#                   a Cloudflare Tunnel. See CLOUDFLARE_TUNNEL_SETUP.md.
#
# The application data (database, logs, data-protection keys) will be stored in:
#   macOS: ~/Library/Application Support/StageZero/
#   Linux: ~/.config/stagezero/
# ═══════════════════════════════════════════════════════════════

set -e

# Detect platform
if [[ "$OSTYPE" == "darwin"* ]]; then
    # macOS
    STAGEZERO_DATA_DIR="$HOME/Library/Application Support/StageZero"
elif [[ "$OSTYPE" == "linux-gnu"* ]]; then
    # Linux
    STAGEZERO_DATA_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/stagezero"
else
    echo "Unsupported platform: $OSTYPE"
    echo "Using default data directory: ./.volumes/stagezero-data"
    STAGEZERO_DATA_DIR="./.volumes/stagezero-data"
fi

# Create data directory if it doesn't exist
mkdir -p "$STAGEZERO_DATA_DIR"

# Default command is 'up', default target is 'debug'
COMMAND="${1:-up}"
TARGET="${2:-debug}"

case "$TARGET" in
    debug)
        COMPOSE_FILE="debug.docker-compose.yml"
        SERVICE="debug-stagezero"
        APP_URL="https://localhost:5000"
        ;;
    prod)
        COMPOSE_FILE="prod.docker-compose.yml"
        SERVICE="prod-stagezero"
        APP_URL="http://127.0.0.1:5100"
        ;;
    *)
        echo "Unknown target: $TARGET (expected 'debug' or 'prod')"
        exit 1
        ;;
esac

echo "═══════════════════════════════════════════════════════════════"
echo "StageZero Docker Compose"
echo "═══════════════════════════════════════════════════════════════"
echo "Platform: $OSTYPE"
echo "Target:   $TARGET ($COMPOSE_FILE)"
echo "Data Directory: $STAGEZERO_DATA_DIR"
echo "═══════════════════════════════════════════════════════════════"
echo ""

# Export the environment variable for docker-compose
export STAGEZERO_DATA_DIR

case "$COMMAND" in
    up)
        echo "Starting StageZero..."
        docker-compose -f "$COMPOSE_FILE" up --build -d
        echo ""
        echo "✅ StageZero is running!"
        echo "   Web UI: $APP_URL"
        echo "   Data:   $STAGEZERO_DATA_DIR"
        echo ""
        echo "To view logs: ./docker-run.sh logs $TARGET"
        echo "To stop:      ./docker-run.sh down $TARGET"
        ;;
    down)
        echo "Stopping StageZero..."
        docker-compose -f "$COMPOSE_FILE" down
        echo "✅ StageZero stopped"
        ;;
    logs)
        docker-compose -f "$COMPOSE_FILE" logs -f "$SERVICE"
        ;;
    restart)
        echo "Restarting StageZero..."
        docker-compose -f "$COMPOSE_FILE" restart "$SERVICE"
        echo "✅ StageZero restarted"
        ;;
    build)
        echo "Building StageZero..."
        docker-compose -f "$COMPOSE_FILE" build
        echo "✅ Build complete"
        ;;
    *)
        echo "Usage: $0 [up|down|logs|restart|build] [debug|prod]"
        echo ""
        echo "Commands:"
        echo "  up       - Start StageZero (default)"
        echo "  down     - Stop StageZero"
        echo "  logs     - View logs"
        echo "  restart  - Restart StageZero"
        echo "  build    - Rebuild Docker image"
        echo ""
        echo "Targets:"
        echo "  debug    - Hot-reload development container (default)"
        echo "  prod     - Release build for use behind a Cloudflare Tunnel"
        exit 1
        ;;
esac
