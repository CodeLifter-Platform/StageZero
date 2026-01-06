#!/bin/bash

# ═══════════════════════════════════════════════════════════════
# SETUP DOCKER DEVELOPMENT CERTIFICATES
# ═══════════════════════════════════════════════════════════════
# This script sets up HTTPS development certificates for Docker
# ═══════════════════════════════════════════════════════════════

set -e

echo "🔐 Setting up HTTPS development certificates for Docker..."
echo ""

# Certificate directory
CERT_DIR="$HOME/.aspnet/https"
CERT_FILE="$CERT_DIR/aspnetapp.pfx"

# Create directory if it doesn't exist
if [ ! -d "$CERT_DIR" ]; then
    echo "📁 Creating certificate directory: $CERT_DIR"
    mkdir -p "$CERT_DIR"
fi

# Clean existing certificates
echo "🧹 Cleaning existing certificates..."
dotnet dev-certs https --clean

# Generate new certificate
echo "🔑 Generating new development certificate..."
dotnet dev-certs https -ep "$CERT_FILE" -p ""

# Trust the certificate
echo "✅ Trusting the certificate..."
dotnet dev-certs https --trust

# Verify certificate was created
if [ -f "$CERT_FILE" ]; then
    echo ""
    echo "✅ Certificate created successfully!"
    echo "📍 Location: $CERT_FILE"
    echo ""
    echo "You can now run StageZero with Docker using:"
    echo "  docker-compose up --build"
    echo ""
    echo "Or use the VS Code launch profile:"
    echo "  Press F5 → Select 'StageZero (Docker Compose)'"
    echo ""
else
    echo ""
    echo "❌ Failed to create certificate!"
    echo "Please check the error messages above."
    exit 1
fi

