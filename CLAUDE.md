# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

PlainSight is a distributed digital signage system built for churches. It uses server-side rendering to convert websites to video, then streams content to Raspberry Pi players over SMB. Players self-update by polling the server heartbeat API.

## Commands

```bash
# Restore dependencies
dotnet restore

# Build entire solution
dotnet build

# Run with .NET Aspire (starts PostgreSQL container + Signage.Server)
dotnet run --project src/PlainSight.AppHost

# Run tests
dotnet test

# Format code
dotnet format

# Run Signage.Server standalone (requires PostgreSQL already running)
dotnet run --project src/Signage.Server

# Add EF Core migration (run from src/Signage.Server)
dotnet ef migrations add <MigrationName>
dotnet ef database update

# Publish Player for Raspberry Pi (ARM64)
dotnet publish src/Signage.Player/Signage.Player.csproj -r linux-arm64 --self-contained -p:PublishSingleFile=true

# Publish Photino Player for Raspberry Pi
dotnet publish src/Signage.Player.Photino/Signage.Player.Photino.csproj -r linux-arm64 --self-contained -p:PublishSingleFile=true

# Build Docker image for server
docker build -t plainsight-server -f src/Signage.Server/Dockerfile .

# Run production stack with Docker Compose
docker compose up -d
```

## Architecture

### Projects

| Project | TFM | Role |
|---|---|---|
| `PlainSight.AppHost` | net10.0 | .NET Aspire orchestrator: provisions PostgreSQL container and wires `Signage.Server` |
| `PlainSight.ServiceDefaults` | net10.0 | Shared Aspire config (OpenTelemetry, health checks, service discovery) |
| `Signage.Server` | net10.0 | Blazor Web App + REST API; admin UI, content management, device fleet |
| `Signage.Player` | net10.0 | Console player for Raspberry Pi; heartbeat, SMB streaming, self-update |
| `Signage.Player.Photino` | **net8.0** | Native-windowed player using Photino.NET; HTML5 video, playlist management |
| `Signage.Shared` | net8.0 + net10.0 | Shared models used by both server and players |

**Important**: `Signage.Player.Photino` targets **net8.0** (not net10.0) because Photino.NET 4.0.16 requires it. Do not change this TFM.

### Key files to read first

- `src/PlainSight.AppHost/AppHost.cs` — Aspire wiring; PostgreSQL uses `ContainerLifetime.Persistent`
- `src/Signage.Server/Data/SignageDbContext.cs` — EF Core context with `Device`, `ContentItem`, `Playlist`, `PlaylistItem`
- `src/Signage.Server/Program.cs` — Service registration; DB migrations run at startup via `Migrate()`
- `src/Signage.Server/Services/VersionService.cs` — Currently hardcoded to `"1.0.0"`; canary deployment logic is a TODO

### Data flow

**Heartbeat cycle (every 30 seconds):**
Player → `POST /api/device/heartbeat` → server upserts Device record → responds with `{ requestScreenshot, updateUrl }` → player acts on commands.

**Self-update:** `updateUrl` in heartbeat response points to `/api/updates/{version}/binary`. Player downloads, swaps binary on disk (keeping `.bak`), then calls `Environment.Exit(0)` so systemd restarts it with the new binary. No signature verification is currently implemented.

**Content delivery:** Files live on a Samba share mounted at `/mnt/signage`. The server writes rendered MP4s there via `ContentController`. Players stream directly from the SMB mount (`/mnt/signage/content`).

**Photino playlist:** `PlaylistService` reads `playlist.json` (with path-traversal validation) or falls back to directory scan. The `PhotinoWindow` uses a custom `app://` scheme handler to serve local video files; path validation prevents traversal outside `_contentPath`.

### REST API summary

| Method | Path | Description |
|---|---|---|
| POST | `/api/device/heartbeat` | Player telemetry; returns update/screenshot commands |
| GET | `/api/device` | List all devices |
| POST | `/api/device/{deviceId}/screenshot` | Set `ScreenshotRequested` flag |
| GET | `/api/content` | List content items |
| POST | `/api/content/render` | Render URL to video via PuppeteerSharp |
| POST | `/api/content/upload` | Upload video/image to SMB share |
| DELETE | `/api/content/{id}` | Remove content |
| GET/POST | `/api/playlists` | Playlist CRUD |

### Blazor pages

All pages in `src/Signage.Server/Components/Pages/` use `@rendermode InteractiveServer`. The `Devices` page auto-refreshes every 5 seconds via a `Timer`. The `Versions` page currently uses in-memory hardcoded data — version management is not yet database-driven.

### Database schema

`SignageDbContext` manages four tables. `Device.DeviceId` (string, unique) is the natural key used in all player/API interactions. `ContentItem.FileName` is unique. `Playlist` → `PlaylistItem` → `ContentItem` with cascade delete on `PlaylistItem` and restrict on `ContentItem`.

### Player environment variables

| Variable | Default | Used by |
|---|---|---|
| `ServerUrl` | `https://localhost:7149/` | Both players |
| `ContentPath` | `/mnt/signage/content` | Photino player only |
| `Debug` | `false` | Photino player (windowed mode) |

## Coding Rules

- **Always use explicit types; never use `var`** — this is a hard requirement from the project conventions.
- Use C# 14 idioms: file-scoped namespaces, `ArgumentNullException.ThrowIfNull`, `Async` suffix on async methods.
- Prefer least visibility: `private`/`internal` before `public`.
- Async methods must accept and thread `CancellationToken` where appropriate.
- No silent catches — log and rethrow, or return errors explicitly.
- Keep diffs minimal; avoid unrelated formatting changes.
- Do not add new public interfaces unless required for DI or testing.
- When adding EF schema changes, always create and apply a migration; do not modify existing migration files.
- `VersionService.GetTargetVersion` is intentionally hardcoded; database-driven canary deployment is an open TODO.
