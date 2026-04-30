# PlainSight

Enterprise-grade digital signage system for churches and organizations, built with .NET 10 and optimized for Raspberry Pi 5.

## 🎯 Overview

PlainSight is a distributed digital signage solution that provides zero-touch maintenance and high reliability through:

- **Server-Side Rendering**: Complex websites rendered to video on the server, not the device
- **Self-Updating Fleet**: Devices automatically update themselves without human intervention
- **SMB Streaming**: Content streamed directly from network share - no local synchronization
- **Live Monitoring**: Real-time device status and on-demand screenshot capture
- **Canary Deployments**: Test updates on specific devices before fleet-wide rollout

## 🚀 Quick Start

### Deploy Server (Docker)

```bash
git clone https://github.com/glensouza/PlainSight.git
cd PlainSight
docker compose up -d
open http://localhost:8080
```

### Deploy Player (Raspberry Pi)

```bash
curl -sSL https://raw.githubusercontent.com/glensouza/PlainSight/main/deployment/raspberry-pi/install.sh | bash
```

## 📚 Documentation

- **[Complete Documentation](docs/README.md)** - Full system overview
- **[Deployment Guide](docs/deployment.md)** - Server deployment with Docker
- **[Raspberry Pi Setup](docs/raspberry-pi-setup.md)** - Player device setup
- **[GitHub Actions](docs/github-actions.md)** - CI/CD pipeline
- **[Architecture](docs/architecture.md)** - System design details
- **[API Reference](docs/api.md)** - REST API documentation

## 🏗️ Architecture

### Components

1. **PlainSight.Server** - Admin web app (ASP.NET Core, Blazor)
   - Device management
   - Content rendering (PuppeteerSharp)
   - Update distribution
   
2. **PlainSight.Player** - Raspberry Pi client (.NET 10)
   - SMB streaming via Chromium kiosk + HTML5 video player
   - Heartbeat reporting
   - Self-updating
   
3. **Infrastructure**
   - PostgreSQL database
   - Samba file share
   - .NET Aspire orchestration

### Technology Stack

- .NET 10 (LTS)
- ASP.NET Core & Blazor
- Entity Framework Core
- PostgreSQL
- Docker & Docker Compose
- .NET Aspire
- PuppeteerSharp
- Chromium (kiosk display on Raspberry Pi)

## 🔧 System Requirements

### Server
- Docker Desktop (macOS/Linux)
- 2GB RAM minimum
- 10GB disk space

### Player
- Raspberry Pi 5 (4GB/8GB)
- Industrial MicroSD (32GB+)
- Active cooling (mandatory)
- Gigabit Ethernet
- HDMI 2.1 display

## 🎓 Features

- ✅ Zero-touch device maintenance
- ✅ Automatic software updates
- ✅ Real-time telemetry
- ✅ Live screenshot capture
- ✅ Canary deployment strategy
- ✅ 4K@60fps video playback
- ✅ 5 previous images retained for rollback
- ✅ SMB-based content delivery

## 📦 Building from Source

```bash
# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run with Aspire (development)
dotnet run --project src/PlainSight.AppHost

# Build Docker image
docker build -t plainsight-server -f src/PlainSight.Server/Dockerfile .

# Build Player for Raspberry Pi
dotnet publish src/PlainSight.Player/PlainSight.Player.csproj \
  -r linux-arm64 --self-contained -p:PublishSingleFile=true
```

## 🤝 Contributing

This is a church digital signage project. Contributions welcome!

## 📄 License

Copyright (c) 2026. All rights reserved.

## 💬 Support

For issues and questions, please open an issue on GitHub.

