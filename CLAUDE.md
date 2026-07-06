# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

PlainSight is a distributed digital signage system built for churches. It uses server-side rendering to convert websites to video, then streams content to Raspberry Pi players over SMB. Players self-update by polling the server heartbeat API.

## Recent Features & Fixes (Issues #96–108)

This batch of issues (merged ~July 2026) significantly expanded the platform's capabilities:

| Issue | Feature | Key Files |
|-------|---------|-----------|
| #96 | **Announcement Entity**: Replaced companion-clip self-pairing with an `Announcement` grouping related media (image + video, ordered, with expiration) | `PlainSightDbContext`, `Announcement.cs`, `PlaylistItem.cs` (composite FK logic) |
| #97 | **Watermark Removal**: Multi-frame detection + CRF 18 encode + position refinement for Veo and Gemini video cleanup | `WatermarkVideoWorkerService`, `SvdGenerationWorkerService` |
| #98 | **Content Expiration**: Expire-by-date on items, auto-sort playlists by event date at serve time, auto-delete expired content after grace period | `ContentItem.ExpiresAt`, `Playlist.SortMode` (enum: `Manual` / `ByEventDate`), `ExpirationCleanupWorkerService` |
| #99 | **Ken Burns 3-layer parallax**: Extended the image-to-video Ken Burns transform from a single optional overlay to a generic loop-based composition of up to 2 overlay layers (graphics + text) over the panning/zooming background; each overlay is static or parallax-scaled via `ParallaxRate` | `VideoProcessorService.KenBurnsAsync`, `KenBurnsOverlayLayer`, `TransformApi` (`/api/content/{id}/ken-burns`), `Content.razor` |
| #100 | **Edit Modal Companion**: Allow setting/changing Announcement when editing video output | Blazor `EditModal.razor`, `VideoEditorService` |
| #104 | **Playback State Machine Hardening**: Fixed race condition causing player to skip/cut-short items; replaced `isSwapping` guard with monotonic `playToken` | `index.html` (`playToken`, state machine logic) |
| #105 | **Emergency Broadcast**: Instant full-screen takeover on all/targeted screens, overlays above playlist content (z-index 500) | `EmergencyBroadcastService`, `EmergencyBroadcastController`, player overlay CSS |
| #106 | **Player Watchdog + Self-Healing**: Detects stalled playback, logs diagnostic emails, reloads page or restarts kiosk via systemd | `PlaybackWatchdogService`, heartbeat field `reloadRequested` |
| #107 | **DB-driven Version Management + Canary**: `VersionService.GetTargetVersionAsync` resolves the target version from the DB (group pin → `Default` pin → newest ingested `PlayerVersion`), cached ~30 s; Versions page manages real `PlayerVersion` rows + per-group assignments with a one-click "Promote to all groups"; rollback works because the player applies any `updateFileName` on version mismatch | `VersionService`, `DeviceApi` (heartbeat `UpdateFileName`), `Versions.razor`, `ManifestReconciler` |
| #108 | **Ticker Overlay**: Scrolling/fading text band (4s rotation), active/time-window/group filtering, server-driven sorting | `TickerMessage` entity, `TickerMessages.razor` admin page, player overlay (z-index 70, below emergency) |

Also merged: #102 (media atomic writes), #103 (companion preview fix), #109–113 (documentation).

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
- `src/PlainSight.Server/Data/PlainSightDbContext.cs` — EF Core context with 18 entity types: `Device`, `ContentItem`, `Playlist`, `PlaylistItem`, `Announcement`, `AnnouncementMedia`, `Schedule`, `ScheduleTargetGroup`, `BrandingVideo`, `BrandingSchedule`, `NdiSource`, `LogEntry`, `PlayerVersion`, `DeviceGroup`, `DeviceGroupVersion`, `DeviceScreenshot`, `SystemSetting`, `AdminUser`
- `src/PlainSight.Server/Services/ExpirationCleanupService.cs` — Removes expired playlist items and optionally deletes files after grace period; runs hourly via `ExpirationCleanupWorkerService`
- `src/PlainSight.Server/Program.cs` — Service registration; DB migrations run at startup via `Migrate()`
- `src/PlainSight.Server/Api/DeviceApi.cs` — Heartbeat endpoint is the system's spine; X-Api-Key TOFU auth, playlist delivery, live-mode decisions, screenshot burst triggers
- `src/PlainSight.Server/Services/ContentSyncService.cs` — Holds `SyncLock` (static `SemaphoreSlim`) across file write + DB insert to prevent unique `FileName` constraint violations (see commit a332c72)
- `src/PlainSight.Server/Services/VersionService.cs` — Reads server version from assembly metadata; `GetTargetVersionAsync` resolves the per-group target version from the DB (group pin → `Default` pin → newest `PlayerVersion`), cached ~30 s for the heartbeat hot path
- `src/PlainSight.Player/wwwroot/index.html` — HTML5 double-buffered video player with cross-fade transitions, playlist polling, branding interstitial, idle fallback, emergency broadcast overlay (z-index 500), ticker message band (z-index 70), and now-playing reporting
- `version.txt` — Single source of truth for `MAJOR.MINOR`; edit this file to bump the major or minor version

### Data flow

**Heartbeat cycle (every 30 seconds):**
Player → `POST /api/device/heartbeat` → server upserts Device record → responds with `HeartbeatResponse` containing `{ requestScreenshot, updateFileName, expectedSha256, assignedApiKey, playlistItems, brandingItem, emergency, tickerMessages, liveMode, ndiSourceName, reloadRequested, logMinLevel, logShipIntervalSeconds, screenshotBurstCount, screenshotBurstIntervalSeconds }` → player acts on commands.

**Self-update:** `updateFileName` in heartbeat response identifies the binary on the share. Player downloads from `/api/updates/{version}/binary`, verifies SHA-256 against `expectedSha256`, swaps binary on disk (keeping `.bak`), then calls `Environment.Exit(0)` so systemd restarts it with the new binary.

**Content delivery:** Files live on a Samba share mounted at `/mnt/plainsight`. The server writes rendered MP4s there. Players maintain a local content cache (`/var/cache/plainsight/content`) synced every heartbeat with SMB fallback (`ContentSyncService` / `CacheManager`), providing offline resilience when the share is unreachable.

**Player display:** `PlainSight.Player` runs an embedded Kestrel web server serving an HTML5 video player page at `/player`. `KioskService` launches the system Chromium browser in kiosk mode pointed at that local page. `PlaylistService` reads `playlist.json` (with path-traversal validation) or falls back to a directory scan of the content path.

### REST API summary

Content management (upload, delete, rename, playlists, schedules) is performed directly through Blazor server-side logic, not REST. The REST surface is player-facing and transform-only.

| Method | Path | Description |
|---|---|---|
| POST | `/api/device/heartbeat` | Player telemetry; returns `{ requestScreenshot, updateFileName, expectedSha256, assignedApiKey, playlistItems, brandingItem, emergency, tickerMessages, liveMode, ndiSourceName, reloadRequested, logMinLevel, logShipIntervalSeconds, screenshotBurstCount, screenshotBurstIntervalSeconds }` |
| POST | `/api/device/{deviceId}/logs` | Player log upload (JSON `DeviceLogBatchDto`, capped at 500 entries, `X-Api-Key` required) |
| POST | `/api/device/{deviceId}/screenshot/notify` | Player notifies server a screenshot was written to SMB (`multipart/form-data` with `fileName` field, `X-Api-Key` required) |
| GET | `/api/media/content/{fileName}` | Serve content file from SMB share |
| GET | `/api/media/idle/{fileName}` | Serve idle/fallback file |
| GET | `/api/media/branding/{fileName}` | Serve branding asset |
| GET | `/api/media/screenshot/{deviceId}/{fileName}` | Serve screenshot |
| POST | `/api/content/{id}/image-to-video` | Convert image to looping video via ffmpeg (`durationSeconds` query param) |
| POST | `/api/content/{id}/extract-frame` | Extract first/last frame from video (`position` query param: `"first"` or `"last"`) |
| POST | `/api/content/{id}/ken-burns` | Generate Ken Burns zoom-pan video from image (JSON body with normalized 0.0–1.0 rects, optional overlay+parallax) |
| GET | `/api/updates/latest/binary` | Download latest player binary (public, no auth) |
| GET | `/api/updates/{version}/binary` | Download specific player version binary (public, no auth) |

Device endpoints (`/heartbeat`, `/logs`, `/screenshot/notify`) use `X-Api-Key` header with trust-on-first-use: a new device gets a key assigned via `assignedApiKey` on its first heartbeat; all later calls must present it or receive `401 Problem`.

### Blazor pages

All pages in `src/PlainSight.Server/Components/Pages/` use `@rendermode InteractiveServer`. The `Devices` page auto-refreshes every 5 seconds via a `Timer`. The `Versions` page is database-driven: it lists real `PlayerVersion` rows (populated by `ManifestReconciler`), manages per-group `DeviceGroupVersion` assignments for canary rollouts, and offers a one-click "Promote to all groups". The `Announcements` page manages `Announcement`/`AnnouncementMedia` records (title, event date, expiration, ordered media); `Playlists` can add an `Announcement` as a playlist item alongside plain `ContentItem`s.

### Database schema

`PlainSightDbContext` manages 18 entity types. `Device.DeviceId` (string, unique) is the natural key used in all player/API interactions. `ContentItem.FileName` is unique. `Playlist` → `PlaylistItem` with cascade delete; a `PlaylistItem` has exactly one of `ContentItemId` or `AnnouncementId` set (nullable FKs, both cascade, enforced by a `CK_PlaylistItems_ExactlyOneTarget` check constraint added via raw SQL in the `AddAnnouncements` migration). `Schedule` has a `Playlist` FK (cascade) and many `ScheduleTargetGroup` entries. `NdiSource.ServiceName` is unique, and `Device` has a nullable FK to `NdiSource`. `BrandingSchedule` references `BrandingVideo` (cascade). `LogEntry` is bulk-inserted from a bounded channel. `PlayerVersion` and `DeviceGroupVersion` store fleet-update metadata. `SystemSetting` is keyed by string. `AdminUser` has a unique `Username`.

`ContentItem` notable fields: `SourceContentItemId` (int?, FK to self — tracks the original item a transform was derived from), `ThumbnailFileName` (string?, sidecar `_thumb.jpg` generated at upload/sync), `EventDate` (`DateOnly?` — the date the content is about, used for playlist sorting), `ExpiresAt` (`DateTime?`, UTC — after this moment the item is not served and may be cleaned up).

`Announcement` groups related media (e.g. an event's image and video) under one record: `Title`, `Description?`, `EventDate` (`DateOnly?`), `ExpiresAt` (`DateTime?`, UTC), plus an ordered `AnnouncementMedia` list (`ContentItemId` + `SortOrder`). Replaces the old `ContentItem.CompanionContentItemId`/`CompanionPosition` self-FK pairing (removed in the `AddAnnouncements` migration, which also converts any existing companion pairs into `Announcement`s and repoints their `PlaylistItem`s). Server expands an `Announcement` playlist item into its ordered media at heartbeat/preview time — no baked MP4.

`Playlist` has a `SortMode` field (enum: `Manual` = preserve `Order`, `ByEventDate` = sort items by their `EventDate`/`Announcement.EventDate` at serve-time, nulls last). When a playlist item's `EventDate` is set and `ExpiresAt` is not, the server defaults `ExpiresAt` to the end of that day (UTC).

### Player environment variables

See [docs/configuration.md](docs/configuration.md) for the complete reference. Key variables an agent most needs:

| Variable | Default | Used by |
|---|---|---|
| `ServerUrl` | `http://plainsight-server` | PlainSight.Player |
| `ContentPath` | `/mnt/plainsight/content` | PlainSight.Player |

### Background workers

| Worker | Interval/Trigger | Role |
|---|---|---|
| `ContentSyncWorkerService` | 30 s | Syncs disk files with `ContentItems` DB table |
| `BrandingSyncWorkerService` | 30 s | Syncs disk files with `BrandingVideos` DB table |
| `ExpirationCleanupWorkerService` | 1 h | Removes expired playlist items and optionally deletes files after grace period |
| `RenderWorkerService` | Queue-driven | Renders websites to MP4 via headless browser |
| `YouTubeDownloadWorkerService` | Queue-driven | Downloads and shrinks YouTube videos |
| `SvdGenerationWorkerService` | Queue-driven | Generates SVD (ComfyUI) image-to-video |
| `WatermarkVideoWorkerService` | Queue-driven | Removes Veo watermarks via ffmpeg |
| `DeviceMonitorService` | Periodic | Sends email alerts for offline devices |
| `AutoScreenshotService` | Configurable | Requests screenshots from all online devices |
| `LogRetentionService` | 24 h | Prunes old `LogEntry` rows |
| `ReconciliationBackgroundService` | 60 s | Ingests new player version manifests from disk |
| `NdiDiscoveryService` | 15 s | Scans mDNS for NDI sources on the network |
| `ObsDiscoveryService` | Event-driven | Monitors OBS WebSocket for live/recording state |

### Versioning

Version format is `MAJOR.MINOR.PATCH` (e.g., `1.0.3`).

**`version.txt`** (repo root) contains only the `MAJOR.MINOR` string (e.g., `1.0`). Editing this file and pushing to `main` triggers both workflows and resets the patch counter to `0`.

**Patch counter** is tracked per-workflow in state files on the self-hosted runner:

| Workflow | State file |
|---|---|
| Server | `~/.plainsight/server-build-state` |
| Player | `~/.plainsight/player-build-state` |

Each state file stores `MAJOR_MINOR=x.y` and `PATCH=n`. On each run the CI reads the file, increments `PATCH` if `MAJOR_MINOR` matches, or resets `PATCH` to `0` if it changed.

**Automatic minor bump on PR (`bump-minor.yml`):** when any PR targeting `main` is opened or reopened, the workflow increments the minor version in `version.txt` on the PR branch (relative to `main`'s current version) and commits it back. Both server and player workflows include `version.txt` in their `paths:` triggers, so merging the PR to `main` automatically kicks off a build for each with patch `0` for the new `MAJOR.MINOR`.

**To manually bump the version:** edit `version.txt`, commit, and push to `main` (or include it in a PR). Both CI workflows will reset patch to `0` on their next run.

**To manually seed or reset a counter** (e.g., after re-imaging the runner): edit the state file directly on the runner, e.g.:
```bash
mkdir -p ~/.plainsight
printf 'MAJOR_MINOR=1.1\nPATCH=0\n' > ~/.plainsight/server-build-state
printf 'MAJOR_MINOR=1.1\nPATCH=0\n' > ~/.plainsight/player-build-state
```

## Coding Rules

### Hard requirements

- **Always use explicit types; never use `var`** — applies to assignments, `foreach`, out-vars, lambdas — everywhere.
- Use C# 14 idioms: file-scoped namespaces, `ArgumentNullException.ThrowIfNull`, `Async` suffix on async methods.
- Prefer least visibility: `private`/`internal` before `public`. Do not add public interfaces unless required for DI or testing.
- Async methods must accept and thread `CancellationToken` where appropriate.
- No silent catches — log and rethrow, or return errors explicitly. Empty catch blocks must use a brace body with a comment explaining what's swallowed (e.g., `catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* expected during shutdown */ }`); never `catch { }` on one line.
- When adding EF schema changes, always create and apply a migration; do not modify existing migration files.
- `VersionService.GetTargetVersionAsync` resolves the target version from the DB and caches it ~30 s; it must never return a hardcoded version string (empty string means "no update target").

### Style rules — apply on every edit

- **`this.` prefix** on all instance field and method access (`this.activeWebSocket`, `this.LoadApiKey()`).
- **No underscore prefix** on private fields (`_logger` → `logger`, `_lock` → `@lock`). `static readonly` and `const` use **PascalCase** (e.g., `CanonicalOptions`, `SmCxvirtualscreen`) — not `_camelCase`, not `ALL_CAPS`.
- **Acronym casing**: acronyms >2 letters use Pascal-style (`OBSDiscoveryService` → `ObsDiscoveryService`, `NDI` → `Ndi`). Two-letter acronyms (`Id`, `Db`) stay as-is.
- **Primary constructors** when no validation is required; use a classical constructor when you need `ArgumentNullException.ThrowIfNull`. Captured fields still don't get underscores.
- **`init` accessors** for properties only set during construction (`Id`, `CreatedAt`, navigation properties set once, JSON DTO/options/manifest types).
- **Computed/getter-only properties use `PascalCase`** (e.g., `onlineCount` → `OnlineCount`).
- **No redundant initializers** like `volatile bool chromiumReady = false;` — drop the `= false`.
- **Collection expressions `[]`** instead of `new List<T>()`, `new()`, `Enumerable.Empty<T>()`, or `new[] { ... }`.
- **`coll.Any()` → `coll.Count != 0`** when `Count` is available.
- **Always brace** `if`/`else`/`foreach`/`using` — no single-line bodies, no single-line early returns.
- **Switch expressions** over if-else chains returning a value; use property patterns (`device is { NdiAutoSwitch: true, AssignedNdiSourceId: not null }`).
- **Invert conditions** to reduce nesting (early-return / `continue` style).
- **`await using`** for any `IAsyncDisposable` — `FileStream(useAsync: true)`, `DbContext`, etc.
- **`System.Threading.Lock`** for lock targets, not `object`. Common name: `@lock` or `processLock`.
- **One top-level type per file** — split helper types into their own files.
- **Collapse multi-line method signatures** to a single line, even if long.
- **Unused lambda params use `_`** (`(s, e) =>` → `(_, e) =>`).
- **`[LibraryImport]` + `partial`** over `[DllImport]` + `extern`.
- **No fully-qualified type names** when a `using` directive can be added.
- **`configuration.GetValue<T>(key, default)` → `configuration.GetValue(key, default)`** when `T` is inferable.
- **Razor `@using` directives** belong in `Components/_Imports.razor`, not per page.
