# PlainSight Architecture

This document describes the technical architecture of the PlainSight digital signage system.

## System Overview

PlainSight uses a distributed architecture with server-side rendering and a unified SMB-centric storage model to ensure reliable 24/7 operation of digital signage displays.

```
┌─────────────────────────────────────────────────────────┐
│              Self-Hosted GitHub Runner (Mac)             │
│  ┌──────────────────┐      ┌──────────────────┐        │
│  │  Build Server    │      │  Build Player    │        │
│  │  Local Docker    │      │  ARM64 Binary    │        │
│  └────────┬─────────┘      └────────┬─────────┘        │
│           │                         │                   │
└───────────┼─────────────────────────┼───────────────────┘
            │                         │
            ▼                         ▼
┌─────────────────────────────────────────────────────────┐
│              Production Server (Docker)                  │
│  ┌──────────────────────────────────────────────────┐  │
│  │  ┌────────────┐  ┌────────────┐  ┌────────────┐ │  │
│  │  │ PostgreSQL │  │  PlainSight│  │ Cloudflare │ │  │
│  │  │  Database  │  │   Server   │  │   Tunnel   │ │  │
│  │  └────────────┘  └────────────┘  └────────────┘ │  │
│  └───────────────────▲──────────────────────────────┘  │
└──────────────────────┼──────────────────────────────────┘
            │          │              │
            │          │              │ 
            │          │              ▼
            │          │      ┌────────────────────┐
            │          └──────┤  External MyCloud  │
            │                 │     SMB Share      │
            │ Heartbeat API   └──────┬─────────────┘
            │ API Keys               │
            ▼                        │ SMB Mount (/mnt/plainsight)
┌────────────────────────────────────┼────────────────────┐
│              Raspberry Pi 5 Players│                    │
│  ┌────────────────┐  ┌─────────────▼──┐  ┌───────────┐ │
│  │   Heartbeat    │  │   SMB-First    │  │SMB-First  │ │
│  │    Service     │  │    Updates     │  │Screenshots│ │
│  └────────────────┘  └────────────────┘  └───────────┘ │
│                   Player Application                     │
└─────────────────────────────────────────────────────────┘
```

## Storage Architecture (SMB-Centric)

The system is designed around a unified file system accessible via SMB to both the Server and Players. This "shared brain" approach minimizes network overhead for large files.

**Mount Point**: `/mnt/plainsight` (Unified across the entire fleet)

**Structure**:
```
/mnt/plainsight
  ├── content/       # Final rendered MP4 videos and playlist.json
  ├── idle/          # Fallback loop content (emergency/offline)
  ├── updates/       # Signed player binaries for self-updating
  └── screenshots/   # Player-uploaded PNG snapshots
```

## Components

### 1. PlainSight.Server (Admin Application)

**Technology**: ASP.NET Core 10, Blazor Web App (Interactive Server)

**Responsibilities**:
- Content rendering (HTML to video via PuppeteerSharp)
- Device fleet monitoring & API key management
- Update manifest signing and distribution coordination
- Screenshot history management

**Deployment Strategy**:
- Built locally on the self-hosted runner (no external registry).
- **Rollback Strategy**: Previous image tagged as `:previous` before update.
- **Health Verification**: Automated 2-minute health check; auto-revert to `:previous` if health check fails.

### 2. PlainSight.Player (Raspberry Pi Client)

**Technology**: .NET 10 Console Application (Optimized for Linux-ARM64)

**Responsibilities**:
- Play video content directly from SMB mount (low overhead)
- Report telemetry to server every 30 seconds
- **SMB-First Updates**: Pull binaries from `/mnt/plainsight/updates` (falls back to HTTP)
- **SMB-First Screenshots**: Save PNGs to `/mnt/plainsight/screenshots/{DeviceId}/` (server notified of path)

### 3. Database (PostgreSQL)

Stores metadata, telemetry history, and fleet configuration.

### 4. Networking & Access

- **Internal**: SMB (CIFS 3.0) for high-bandwidth media distribution.
- **External**: Cloudflare Tunnel for secure admin dashboard access without open ports.
- **Security**: ECDSA P-256 signing for all update binaries; mandatory SHA-256 verification before player execution.

## Data Flow

### 1. Unified Update Flow
1. GitHub Action builds Linux-ARM64 binary on the Mac runner.
2. Runner signs manifest and writes both binary + manifest to the MyCloud `updates/` folder.
3. Player heartbeat receives new version info.
4. Player checks `/mnt/plainsight/updates/` first. If found, it copies and verifies; otherwise, it downloads via HTTP.

### 2. Optimized Screenshot Flow
1. Admin clicks "Request Screenshot" in Dashboard.
2. Player receives request via next Heartbeat.
3. Player captures screen and writes PNG directly to MyCloud `screenshots/{DeviceId}/`.
4. Player notifies Server via API: "I just dropped `screenshot_123.png` on the share."
5. Server verifies and adds to history.

## Performance & Reliability

- **Zero-Touch Maintenance**: Players self-heal and self-update via systemd.
- **Decoupled Playback**: If the Server API goes down, Players continue playing from the SMB share using their local `playlist.json` cache.
- **Hardware Acceleration**: Players use the Raspberry Pi 5 VideoCore VII for smooth 4K H.264 decoding.
