# PlainSight Digital Signage System

PlainSight is an enterprise-grade digital signage system designed for churches and other organizations, built with .NET 10 and optimized for Raspberry Pi 5 devices.

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Features](#features)
- [Quick Start](#quick-start)
- [Documentation](#documentation)

## Overview

PlainSight provides a zero-touch maintenance digital signage solution with:

- **Server-Side Rendering**: Web content is rendered on a central server and streamed to devices
- **Self-Updating Players**: Raspberry Pi devices automatically update themselves
- **Live Monitoring**: Real-time playback status and screenshot capture
- **Content Normalization**: All content normalized to H.264/MP4 for smooth playback
- **Hybrid Storage**: SMB share streaming with local content cache for offline resilience

## Architecture

The system consists of three main components:

### 1. PlainSight.Server (Admin Web Application)
- **Technology**: ASP.NET Core 10, Blazor Web App
- **Database**: PostgreSQL
- **Deployment**: Docker container
- **Functions**:
  - Device fleet management
  - Content rendering (PuppeteerSharp)
  - Update distribution
  - Telemetry collection

### 2. PlainSight.Player (Raspberry Pi Application)
- **Technology**: .NET 10 Console Application
- **Platform**: Raspberry Pi 5 (ARM64)
- **OS**: Raspberry Pi OS Lite with labwc (Wayland)
- **Functions**:
  - SMB content streaming
  - Heartbeat reporting
  - Screenshot capture
  - Self-updating

### 3. Infrastructure
- **PostgreSQL Database**: Device state and configuration
- **Samba File Share**: Content distribution via SMB
- **.NET Aspire**: Orchestration and service discovery

## Features

### Zero-Touch Maintenance
- Devices automatically download and apply updates
- Self-healing fleet with automatic restarts
- No manual intervention required

### Live Visibility
- Real-time device status dashboard
- On-demand screenshot capture from any screen
- Playback monitoring and telemetry

### Content Management
- Server-side web content rendering to H.264/MP4
- Playlists with drag-and-drop ordering and per-item duration overrides
- Time/day schedules with prioritization and target device groups
- Branding interstitials between playlist loop passes
- Idle content fallback when no schedule is active
- SMB share with local player cache for offline resilience

### Live Video & NDI
- NDI source auto-discovery via mDNS
- Per-device NDI assignment with auto-switch on source presence
- OBS WebSocket integration for live/recording state sync
- Manual override for force-on/force-off per device

### Media Transforms
- Image-to-video conversion with configurable duration
- Ken Burns zoom-pan with optional overlay and parallax
- Video editing: trim, crop, reverse, speed (0.5x–2.0x), strip audio, compress
- Frame extraction (first/last)
- YouTube download with size/duration limits and automatic re-encode
- AI video generation: Gemini/Veo animation + SVD (ComfyUI) self-hosted option
- Veo watermark removal via ffmpeg
- Announcements: group related media (e.g. an event's image + video) into one ordered playlist item

### Fleet Operations
- Device offline email alerts
- Auto-screenshot bursts on schedule change
- Device log shipping with configurable minimum level
- Fleet update version management
- Canary deployment schema groundwork (`DeviceGroupVersion`); graduated rollout is planned (TODO)

## Quick Start

### Prerequisites
- Docker Desktop (macOS or Linux)
- PostgreSQL support
- .NET 10 SDK (for development)
- Raspberry Pi 5 with active cooling

### Deploy Server (Docker)

```bash
# Clone the repository
git clone https://github.com/glensouza/PlainSight.git
cd PlainSight

# Set environment variables (optional)
export POSTGRES_PASSWORD=your_secure_password

# Start the server with Docker Compose
docker compose up -d

# Access the admin interface
open http://localhost:8080
```

### Deploy Player (Raspberry Pi)

See [Raspberry Pi Setup Guide](raspberry-pi-setup.md) for detailed instructions.

Quick install:
```bash
curl -sSL https://raw.githubusercontent.com/glensouza/PlainSight/main/deployment/raspberry-pi/install.sh | bash
```

## Documentation

- [Deployment Guide](deployment.md) - Server deployment instructions
- [Raspberry Pi Setup](raspberry-pi-setup.md) - Device setup guide
- [GitHub Actions Workflow](github-actions.md) - CI/CD pipeline
- [Architecture Overview](architecture.md) - System design details
- [API Documentation](api.md) - REST API reference
- [Configuration Reference](configuration.md) - All server/player config keys
- [Development Guide](development.md) - Local development setup
- [NDI & OBS Setup](NDI-OBS-Setup.md) - Live video integration
- [Network & Cloudflare](NETWORK-MANAGEMENT.md) - Network topology and Cloudflare tunnel
- [Security](SECURITY.md) - Security considerations and checklist
- [Boot Splash](boot-splash.md) - Boot screen customization
- [Gmail Setup](gmail-setup.md) - SMTP alert email configuration

### Task-Oriented Guides
- [Content Management](guides/content-management.md) - Upload, organize, and manage media
- [Media Transforms](guides/media-transforms.md) - Image-to-video, Ken Burns, video editing
- [AI Media Workflow](guides/ai-media-workflow.md) - Gemini/Veo and SVD animation pipelines
- [Playlists, Schedules & Branding](guides/playlists-schedules-branding.md) - Content programming
- [Live Video](guides/live-video.md) - NDI live mode and OBS integration
- [YouTube Download](guides/youtube-download.md) - Downloading videos from YouTube

## System Requirements

### Server
- Docker Desktop
- 2GB RAM minimum
- 10GB disk space
- macOS or Linux

### Raspberry Pi Device
- Raspberry Pi 5 (4GB or 8GB)
- Industrial MicroSD card (32GB+, SLC/pSLC)
- Official Active Cooler (mandatory)
- Gigabit Ethernet connection
- HDMI 2.1 display

## Technology Stack

- **.NET 10**: Latest LTS framework
- **ASP.NET Core**: Web server and API
- **Blazor**: Interactive web UI
- **Entity Framework Core**: Database ORM
- **PostgreSQL**: Relational database
- **PuppeteerSharp**: Web content rendering
- **.NET Aspire**: Cloud-native orchestration
- **Docker**: Container platform
- **Samba**: File sharing protocol

## License

Copyright (c) 2026. All rights reserved.

## Support

For issues and questions, please open an issue on GitHub.
