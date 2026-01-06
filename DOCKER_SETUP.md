# Docker Setup Guide

This guide explains how to run StageZero using Docker and Docker Compose.

## 🐳 Prerequisites

- Docker Desktop installed and running
- Docker Compose (included with Docker Desktop)
- .NET development certificates (for HTTPS support)

## 🔐 Setup HTTPS Certificates (First Time Only)

Before running the app with HTTPS support, you need to set up development certificates.

### Easy Setup (Recommended)

Run the provided setup script:

```bash
./setup-docker-certs.sh
```

This will automatically:
- Clean existing certificates
- Generate a new development certificate
- Trust the certificate
- Save it to `~/.aspnet/https/aspnetapp.pfx`

### Manual Setup

If you prefer to set up manually:

```bash
# Generate and trust the .NET development certificate
dotnet dev-certs https --clean
dotnet dev-certs https -ep ~/.aspnet/https/aspnetapp.pfx -p ""
dotnet dev-certs https --trust
```

This creates a certificate at `~/.aspnet/https/aspnetapp.pfx` which is mounted into the Docker container.

**Note**: The password is empty (`""`) by default. If you want to use a password, update the `ASPNETCORE_Kestrel__Certificates__Default__Password` environment variable in `docker-compose.yml`.

## 🚀 Quick Start

### Option 1: Using VS Code Launch Profiles

The easiest way to run with Docker:

#### **StageZero (Docker Compose)** ⭐ Recommended

1. Open VS Code
2. Press `F5` or go to Run and Debug
3. Select **"StageZero (Docker Compose)"** from the dropdown
4. Click the green play button

This will:
- Build the Docker image with debugging support
- Start the container with proper configuration
- Attach the debugger automatically
- Enable breakpoints and step-through debugging
- Mount volumes for data persistence
- Load environment variables from `.env`
- Open the app in your browser at http://localhost:5000

#### **Docker Compose (Simple)** - Alternative

1. Press `F5` in VS Code
2. Select **"Docker Compose (Simple)"**

This will:
- Stop any existing containers
- Build the Docker image
- Start the container
- Show logs in the integrated terminal
- Open the app in your browser at http://localhost:5000

### Option 2: Using Command Line

```bash
# Start the application
docker-compose up --build

# Or run in detached mode (background)
docker-compose up --build -d

# View logs
docker-compose logs -f app

# Stop the application
docker-compose down
```

## 📁 Docker Compose Configuration

The `docker-compose.yml` file defines:

- **Service**: `app` (StageZero application)
- **Ports**:
  - `5000:80` - HTTP
  - `5001:443` - HTTPS
- **Volumes**:
  - `./.volumes/data:/app/data` - Persists SQLite database
  - `./StageZero:/app` - Source code (for hot reload in debug mode)
  - `~/.aspnet/https:/https:ro` - Development certificates (read-only)
- **Environment**: Development mode with hot reload enabled

## 🔧 Available VS Code Tasks

You can run these tasks from VS Code (Terminal → Run Task):

- **docker-compose-up** - Start containers in background
- **docker-compose-down** - Stop and remove containers
- **docker-compose-logs** - View live logs

## 🐛 Debugging with Docker

### Full Debugging Support ⭐ Recommended

Use the **"StageZero (Docker Compose)"** launch profile:

1. Set breakpoints in your code
2. Press `F5` in VS Code
3. Select **"StageZero (Docker Compose)"**
4. Debugger attaches automatically
5. Debug like a normal .NET app!

Features:
- ✅ Breakpoints work
- ✅ Step through code (F10, F11)
- ✅ Inspect variables
- ✅ Watch expressions
- ✅ Call stack navigation
- ✅ Hot reload enabled

### Attach Debugger (Advanced)

For manual attachment:

1. Start containers: `docker-compose up -d`
2. In VS Code, select **"Docker Compose"** launch profile
3. Press `F5` to attach debugger
4. Set breakpoints in your code

Note: This requires the container to have the VS debugger installed.

## 📂 Data Persistence

The SQLite database and all its associated files are stored in `./.volumes/data/` on your host machine. This ensures your data persists even when containers are removed.

### SQLite Files Persisted

The volume mount `./.volumes/data:/app/data` automatically persists **all** SQLite files:

- **`stagezero.db`** - Main database file
- **`stagezero.db-shm`** - Shared memory file (for WAL mode)
- **`stagezero.db-wal`** - Write-ahead log file (transaction log)

All three files are required for proper SQLite operation and are automatically created and managed by SQLite.

```bash
# View all database files
ls -la .volumes/data/

# Example output:
# stagezero.db
# stagezero.db-shm
# stagezero.db-wal

# Backup all database files
tar -czf backup-$(date +%Y%m%d).tar.gz .volumes/data/

# Or backup just the main database (when app is stopped)
cp .volumes/data/stagezero.db ./backup-$(date +%Y%m%d).db
```

**Important**: When backing up a running database, always backup all three files together, or stop the container first to ensure consistency.

## 🔄 Hot Reload

In debug mode, the application uses `dotnet watch` which automatically reloads when you change code files. Just save your changes and the app will restart.

## 🏗️ Build Stages

The Dockerfile has multiple stages:

- **debug** - Development with hot reload (default)
- **release** - Production optimized build

To use release mode:

```yaml
# In docker-compose.yml, change:
target: release
```

## 🌐 Environment Variables

Create a `.env` file in the project root for sensitive configuration:

```env
# Email Configuration
Email__SmtpHost=smtp.gmail.com
Email__SmtpPort=587
Email__FromEmail=your-email@gmail.com
Email__FromName=StageZero
Email__Username=your-email@gmail.com
Email__Password=your-app-password

# Cloudflare (if using)
Cloudflare__ApiToken=your-cloudflare-token
```

The `.env` file is automatically loaded by docker-compose.

## 🔍 Troubleshooting

### HTTPS Certificate Issues

If you get certificate errors:

```bash
# Regenerate the certificate
dotnet dev-certs https --clean
dotnet dev-certs https -ep ~/.aspnet/https/aspnetapp.pfx -p ""
dotnet dev-certs https --trust

# Verify the certificate exists
ls -la ~/.aspnet/https/aspnetapp.pfx

# Restart containers
docker-compose down
docker-compose up
```

### Port Already in Use

```bash
# Find what's using port 5000
lsof -i :5000

# Kill the process or change ports in docker-compose.yml
```

### Container Won't Start

```bash
# View detailed logs
docker-compose logs app

# Rebuild from scratch
docker-compose down
docker-compose build --no-cache
docker-compose up
```

### Database Locked

If you get "database is locked" errors:

```bash
# Stop all containers
docker-compose down

# Check if any processes are using the database
lsof .volumes/data/stagezero.db* 2>/dev/null || echo "No processes found"

# If needed, remove the WAL and SHM files (only when container is stopped!)
rm -f .volumes/data/stagezero.db-wal
rm -f .volumes/data/stagezero.db-shm

# Restart
docker-compose up
```

**Note**: The `.db-shm` and `.db-wal` files are temporary and will be recreated automatically. Only delete them when the container is stopped.

### Hot Reload Not Working

Make sure the volume mount is correct in `docker-compose.yml`:
```yaml
volumes:
  - ./StageZero:/app  # This line enables hot reload
```

## 🧹 Cleanup

```bash
# Stop and remove containers
docker-compose down

# Remove containers and volumes
docker-compose down -v

# Remove images
docker-compose down --rmi all

# Complete cleanup (removes database!)
docker-compose down -v --rmi all
rm -rf .volumes/
```

## 📚 Additional Resources

- [Docker Documentation](https://docs.docker.com/)
- [Docker Compose Documentation](https://docs.docker.com/compose/)
- [VS Code Docker Extension](https://marketplace.visualstudio.com/items?itemName=ms-azuretools.vscode-docker)

## 🎯 Production Deployment

For production, use the release build:

```bash
# Build release image
docker-compose build --build-arg target=release

# Run in production mode
docker-compose -f docker-compose.yml -f docker-compose.prod.yml up -d
```

Consider using:
- Proper secrets management (not .env files)
- Reverse proxy (nginx, Traefik)
- SSL certificates (Let's Encrypt)
- Container orchestration (Kubernetes, Docker Swarm)

