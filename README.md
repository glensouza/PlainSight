# PlainSight

Enterprise-grade digital signage system for organizations, built with .NET 10 and optimized for Raspberry Pi 5.

## 🎯 Overview

PlainSight is a distributed digital signage solution that provides zero-touch maintenance and high reliability through server-side rendering, SMB-based content streaming, and automated self-updating fleets.

---

## 🏗️ Software Architecture

PlainSight follows a modern, decoupled architecture designed for high availability and low maintenance overhead.

### 1. Hybrid Server Model
The system uses a unique hybrid interaction model:
- **Internal Logic (Blazor Interactive Server)**: The administrative dashboard uses direct database access via `IDbContextFactory<PlainSightDbContext>`. This eliminates the overhead and security complexity of an internal API loop for UI tasks.
- **External API (Minimal APIs)**: A dedicated set of REST endpoints exists strictly for physical device communication. This "Edge-Only API" surface ensures that Raspberry Pi players have a secure, authenticated channel for heartbeats and content coordination.

### 2. Rendering Engine
Instead of requiring high-performance GPUs on player devices, complex web content (dashboards, animated signs) is rendered to high-quality MP4/WebM video on the server using **PuppeteerSharp**. This ensures a consistent, fluid visual experience across all screens regardless of device age.

### 3. Distribution & Playback Control
- **Explicit Content Control**: The system uses a "No Fallback" policy for main content. Players only display content explicitly assigned to their active playlist via the **Schedules** system.
- **Idle Content Library**: A dedicated "Idle" system provides branded signage when no schedules are active. Files placed in the `idle` folder on the SMB share are automatically synced to the Pi's local cache and played in alphabetical order during unscheduled time slots. This ensures the screen is never blank, even if the network is down.
- **Command & Control**: Heartbeat-based polling (every 30s) where devices report health and receive commands (screenshot, update, playlist changes).
- **Data Plane**: Content is served via **SMB (Samba)**. Players stream video directly from the network share, with local caching for offline resilience.

---

## 📂 File Purpose & Project Structure

### `src/PlainSight.Server` (The Control Plane)
The heart of the system, handling administration and device coordination.
- **`Api/`**: REST endpoints for Raspberry Pi players (Heartbeat, Screenshot Upload, etc.).
- **`Components/Pages/`**: The Admin Dashboard. All logic here executes server-side with direct DB access.
  - **`Schedules.razor`**: Manage time-based playlist assignments with priorities.
- **`Data/`**: EF Core context and database configuration.
- **`Services/`**: Core background logic, including `ScheduleService.cs` (priority calculation) and `WebsiteRecorder.cs` (rendering).

### `src/PlainSight.Player` (The Edge)
A lightweight .NET 10 background worker running on the Raspberry Pi.
- **`PlayerWorker.cs`**: Orchestrates the local Chromium kiosk and handles the heartbeat loop.
- **`Services/`**: Local logic for update application, screen capture, and multi-folder cache syncing.
- **`wwwroot/index.html`**: A hardware-accelerated HTML5 video player.
  - **Playback Mode**: Automatically switches between **Scheduled** and **Idle** content based on the server's control signals.

### `src/PlainSight.Shared`
Shared POCOs and DTOs used by both the Server and the Player to ensure type-safe communication.

### `src/PlainSight.AppHost`
.NET Aspire orchestrator that spins up the Server, Player, and PostgreSQL containers during development.

---

## 🛠️ Infrastructure

### Data Storage
- **PostgreSQL**: Stores device telemetry, playlist metadata, and user accounts.
- **Samba (SMB)**: Used as the CDN. Contains two primary shares:
  - `content/`: Managed library items and rendered videos.
  - `idle/`: Branded fallback content played when no schedule is active.

### Deployment & Orchestration
- **Docker**: The Server project is containerized for easy deployment.
- **Systemd**: The Player runs as a hardened systemd service on Raspberry Pi OS.
- **.NET Aspire**: Used for local development orchestration.

---

## 🔧 Technology Stack

- **Framework**: .NET 10 (C# 13)
- **UI**: Blazor Web App (Interactive Server)
- **Database**: Entity Framework Core & PostgreSQL
- **Rendering**: PuppeteerSharp (Headless Chromium)
- **Authentication**: Cookie-based Auth for Admins; API Key (SHA256) for Devices.
- **Client**: Raspberry Pi 5 running Chromium in Kiosk Mode.

---

## 🚀 Building & Running

```bash
# Run the entire ecosystem locally (Aspire)
dotnet run --project src/PlainSight.AppHost

# Build the Server Docker image
docker build -t plainsight-server -f src/PlainSight.Server/Dockerfile .

# Build the Player for ARM64 (Raspberry Pi)
dotnet publish src/PlainSight.Player/PlainSight.Player.csproj \
  -r linux-arm64 --self-contained -p:PublishSingleFile=true
```

## 📚 Further Reading

- [API Reference](docs/api.md)
- [Architecture Deep-Dive](docs/architecture.md)
- [Raspberry Pi Setup](docs/raspberry-pi-setup.md)
