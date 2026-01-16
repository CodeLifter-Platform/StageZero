#!/bin/bash

# ═══════════════════════════════════════════════════════════════
# StageZero Docker Run Script
# ═══════════════════════════════════════════════════════════════
# This script sets up platform-specific data directories and runs
# Docker Compose with the correct volume mounts.
#
# Usage:
#   ./docker-run.sh [up|down|logs|restart]
#
# The application data (database, logs) will be stored in:
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

echo "═══════════════════════════════════════════════════════════════"
echo "StageZero Docker Compose"
echo "═══════════════════════════════════════════════════════════════"
echo "Platform: $OSTYPE"
echo "Data Directory: $STAGEZERO_DATA_DIR"
echo "═══════════════════════════════════════════════════════════════"
echo ""

# Export the environment variable for docker-compose
export STAGEZERO_DATA_DIR

# Default command is 'up'
COMMAND="${1:-up}"

case "$COMMAND" in
    up)
        echo "Starting StageZero..."
        docker-compose -f debug.docker-compose.yml up --build -d
        echo ""
        echo "✅ StageZero is running!"
        echo "   Web UI: http://localhost:5000"
        echo "   Data:   $STAGEZERO_DATA_DIR"
        echo ""
        echo "To view logs: ./docker-run.sh logs"
        echo "To stop:      ./docker-run.sh down"
        ;;
    down)
        echo "Stopping StageZero..."
        docker-compose -f debug.docker-compose.yml down
        echo "✅ StageZero stopped"
        ;;
    logs)
        docker-compose -f debug.docker-compose.yml logs -f stagezero
        ;;
    restart)
        echo "Restarting StageZero..."
        docker-compose -f debug.docker-compose.yml restart stagezero
        echo "✅ StageZero restarted"
        ;;
    build)
        echo "Building StageZero..."
        docker-compose -f debug.docker-compose.yml build
        echo "✅ Build complete"
        ;;
    *)
        echo "Usage: $0 [up|down|logs|restart|build]"
        echo ""
        echo "Commands:"
        echo "  up       - Start StageZero (default)"
        echo "  down     - Stop StageZero"
        echo "  logs     - View logs"
        echo "  restart  - Restart StageZero"
        echo "  build    - Rebuild Docker image"
        exit 1
        ;;
esac

