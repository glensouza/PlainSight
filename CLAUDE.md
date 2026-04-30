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

# Run with .NET Aspire (starts PostgreSQL container + PlainSight.Server)
dotnet run --project src/PlainSight.AppHost

# Run tests
dotnet test

# Format code
dotnet format

# Run PlainSight.Server standalone (requires PostgreSQL already running)
dotnet run --project src/PlainSight.Server

# Add EF Core migration (run from src/PlainSight.Server)
dotnet ef migrations add <MigrationName>
dotnet ef database update

# Publish Player for Raspberry Pi (ARM64)
dotnet publish src/PlainSight.Player/PlainSight.Player.csproj -r linux-arm64 --self-contained -p:PublishSingleFile=true

# Build Docker image for server
docker build -t plainsight-server -f src/PlainSight.Server/Dockerfile .

# Run production stack with Docker Compose
docker compose up -d
```

## Architecture

### Projects

| Project | TFM | Role |
|---|---|---|
| `PlainSight.AppHost` | net10.0 | .NET Aspire orchestrator: provisions PostgreSQL container and wires `PlainSight.Server` |
| `PlainSight.ServiceDefaults` | net10.0 | Shared Aspire config (OpenTelemetry, health checks, service discovery) |
| `PlainSight.Server` | net10.0 | Blazor Web App + REST API; admin UI, content management, device fleet |
| `PlainSight.Player` | net10.0 | Raspberry Pi player; heartbeat, SMB streaming, self-update, Chromium kiosk display |
| `PlainSight.Shared` | net10.0 | Shared models used by both server and player |

### Key files to read first

- `src/PlainSight.AppHost/AppHost.cs` — Aspire wiring; PostgreSQL uses `ContainerLifetime.Persistent`
- `src/PlainSight.Server/Data/PlainSightDbContext.cs` — EF Core context with `Device`, `ContentItem`, `Playlist`, `PlaylistItem`
- `src/PlainSight.Server/Program.cs` — Service registration; DB migrations run at startup via `Migrate()`
- `src/PlainSight.Server/Services/VersionService.cs` — Currently hardcoded to `"1.0.0"`; canary deployment logic is a TODO

### Data flow

**Heartbeat cycle (every 30 seconds):**
Player → `POST /api/device/heartbeat` → server upserts Device record → responds with `{ requestScreenshot, updateUrl }` → player acts on commands.

**Self-update:** `updateUrl` in heartbeat response points to `/api/updates/{version}/binary`. Player downloads, swaps binary on disk (keeping `.bak`), then calls `Environment.Exit(0)` so systemd restarts it with the new binary. No signature verification is currently implemented.

**Content delivery:** Files live on a Samba share mounted at `/mnt/signage`. The server writes rendered MP4s there via `ContentController`. Players stream directly from the SMB mount (`/mnt/signage/content`).

**Player display:** `PlainSight.Player` runs an embedded Kestrel web server serving an HTML5 video player page at `/player`. `KioskService` launches the system Chromium browser in kiosk mode pointed at that local page. `PlaylistService` reads `playlist.json` (with path-traversal validation) or falls back to a directory scan of the content path.

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

All pages in `src/PlainSight.Server/Components/Pages/` use `@rendermode InteractiveServer`. The `Devices` page auto-refreshes every 5 seconds via a `Timer`. The `Versions` page currently uses in-memory hardcoded data — version management is not yet database-driven.

### Database schema

`PlainSightDbContext` manages four tables. `Device.DeviceId` (string, unique) is the natural key used in all player/API interactions. `ContentItem.FileName` is unique. `Playlist` → `PlaylistItem` → `ContentItem` with cascade delete on `PlaylistItem` and restrict on `ContentItem`.

### Player environment variables

| Variable | Default | Used by |
|---|---|---|
| `ServerUrl` | `http://plainsight-server` | PlainSight.Player |
| `ContentPath` | `/mnt/signage/content` | PlainSight.Player |

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
