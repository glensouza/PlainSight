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
- `src/PlainSight.Server/Services/VersionService.cs` — Reads version from assembly metadata set by CI; canary deployment logic is a TODO
- `version.txt` — Single source of truth for `MAJOR.MINOR`; edit this file to bump the major or minor version

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
- `VersionService.GetTargetVersion` is intentionally hardcoded; database-driven canary deployment is an open TODO.

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
