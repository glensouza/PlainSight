# PlainSight

[![Server CI/CD](https://github.com/glensouza/PlainSight/actions/workflows/server.yml/badge.svg?branch=main)](https://github.com/glensouza/PlainSight/actions/workflows/server.yml)
[![Player CI/CD](https://github.com/glensouza/PlainSight/actions/workflows/player.yml/badge.svg?branch=main)](https://github.com/glensouza/PlainSight/actions/workflows/player.yml)

Enterprise-grade digital signage system for organizations, built with .NET 10 and optimized for Raspberry Pi 5.

## Name

**PlainSight**: References Habakkuk 2:2: "Write the vision; make it plain on tablets, so he may run who reads it." This is arguably the most biblically accurate name for digital signage!

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

### 3. Unified SMB Data Plane
The system is built around a "Shared Brain" storage model using an external SMB share (e.g., MyCloud).
- **SMB-First Updates**: Players pull update binaries directly from the SMB share, falling back to HTTP only if the mount is unavailable.
- **SMB-First Screenshots**: Players write display snapshots directly to the share, drastically reducing network payload sizes when reporting status to the server.
- **Content Streaming**: Players stream video directly from the network share, with local caching for offline resilience.

---

## 📂 Project Structure

### `src/PlainSight.Server` (The Control Plane)
The heart of the system, handling administration and device coordination.
- **`Api/`**: REST endpoints for Raspberry Pi players.
- **`Services/`**: Core background logic, including `WebsiteRecorder.cs` (rendering) and `VersionService.cs` (fleet management).

### `src/PlainSight.Player` (The Edge)
A lightweight .NET 10 background worker optimized for ARM64 Linux.
- **`PlayerWorker.cs`**: Orchestrates the local Chromium kiosk and handles the heartbeat loop.
- **`Services/`**: Local logic for SMB-first updates, screen capture, and cache syncing.

---

## 🛠️ Infrastructure

### Data Storage
- **PostgreSQL**: Stores device telemetry, playlist metadata, and user accounts.
- **External SMB (CIFS)**: Central storage for all media, updates, and screenshots. Mounted at `/mnt/plainsight` across the entire fleet.

### Deployment & Orchestration
- **Local-First CI/CD**: Docker images are built locally on a self-hosted Mac runner and deployed with automated health-check rollbacks.
- **Systemd**: The Player runs as a hardened systemd service on Raspberry Pi OS.
- **Cloudflare Tunnel**: Securely exposes the admin dashboard without port forwarding.

---

## 🔧 Technology Stack

- **Framework**: .NET 10 (C# 14)
- **UI**: Blazor Web App (Interactive Server Mode)
- **Database**: EF Core & PostgreSQL 17
- **Rendering**: PuppeteerSharp (Headless Chromium)
- **Authentication**: Cookie-based Auth for Admins; SHA256 API Keys for Devices.
- **Client**: Raspberry Pi 5 (ARM64) running LabWC/Chromium.

---

## 🚀 Quick Start

```bash
# Build the Player for ARM64 (Raspberry Pi)
dotnet publish src/PlainSight.Player/PlainSight.Player.csproj \
  -r linux-arm64 --self-contained -p:PublishSingleFile=true
```

## 📚 Further Reading

- [Architecture Deep-Dive](docs/architecture.md)
- [Deployment Guide](docs/deployment.md)
- [API Reference](docs/api.md)
- [Raspberry Pi Setup](docs/raspberry-pi-setup.md)
