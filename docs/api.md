# PlainSight API Documentation

> Generated from code. Source files: `src/PlainSight.Server/Api/DeviceApi.cs`, `TransformApi.cs`, `ContentApi.cs`, `UpdateApi.cs`, and DTOs in `src/PlainSight.Shared/Models/`. When updating this document, re-derive from those files — do not patch.

## Base URL

```
http://localhost:8080/api
```

---

## X-Api-Key Authentication (Trust-On-First-Use)

Device endpoints use `X-Api-Key` header authentication with a trust-on-first-use (TOFU) lifecycle:

1. **Initial heartbeat** — A new device sends `POST /api/device/heartbeat` without an `X-Api-Key` header. The server creates the device record, generates a GUID v7 API key, and returns it in the `assignedApiKey` field of the heartbeat response.
2. **Subsequent calls** — Every later call from the device (`/heartbeat`, `/logs`, `/screenshot/notify`) must include `X-Api-Key: <the-key>`. Missing or mismatched keys receive a `401` response with `application/problem+json` body.
3. **Persistence** — The key is stored in the `Device.ApiKey` column in the database. It never expires, but can be rotated by an admin clearing the key (next heartbeat re-registers).

A device that registered but lost its key must have its `ApiKey` cleared on the server before it can re-register.

---

## Device API

### POST /api/device/heartbeat

Player telemetry heartbeat. Creates or upserts the device record and returns commands, playlist, and configuration.

**Auth:** `X-Api-Key` header (optional on first call for TOFU registration; required thereafter).

**Request body** (`DeviceTelemetryDto`):
```json
{
  "deviceId": "plainsight-sanctuary",
  "appVersion": "1.18.0",
  "currentFileName": "worship.mp4",
  "callbackUrl": "http://192.168.1.100:5555",
  "timestamp": "2026-06-17T03:00:00Z"
}
```

**Response** (`HeartbeatResponse`, 200):
```json
{
  "requestScreenshot": false,
  "updateFileName": "plain-sight-player-linux-arm64",
  "expectedSha256": "a1b2c3d4e5f6...",
  "assignedApiKey": "0192abcd1234...",
  "playlistItems": [
    { "fileName": "announcement.mp4", "durationSeconds": 30 },
    { "fileName": "worship.mp4", "durationSeconds": 300 }
  ],
  "brandingItem": { "fileName": "logo.mp4", "durationSeconds": 5 },
  "liveMode": false,
  "ndiSourceName": null,
  "logMinLevel": 4,
  "logShipIntervalSeconds": 60,
  "screenshotBurstCount": null,
  "screenshotBurstIntervalSeconds": null
}
```

| Field | Type | Description |
|---|---|---|
| `requestScreenshot` | `bool` | Server has requested a screenshot capture. Player should take one and call `/screenshot/notify`. Clears after this response. |
| `updateFileName` | `string?` | Filename of the update binary on the share (or `null` if up to date). |
| `expectedSha256` | `string?` | SHA-256 hash the player must verify before applying the update. |
| `assignedApiKey` | `string?` | New API key assigned on first heartbeat (`null` for already-registered devices). |
| `playlistItems` | `PlaylistItemDto[]?` | Active schedule's playlist, already expanded for companion items. `null` if no active schedule. |
| `brandingItem` | `PlaylistItemDto?` | Active branding video to play between playlist loop passes. |
| `liveMode` | `bool` | Player should switch to live NDI viewer instead of cached signage. |
| `ndiSourceName` | `string?` | NDI source name to connect to (only set when `liveMode` is `true`). |
| `logMinLevel` | `int?` | Minimum `LogLevel` the player should ship. `null` means no change. |
| `logShipIntervalSeconds` | `int?` | How often the player should flush buffered logs. |
| `screenshotBurstCount` | `int?` | Number of screenshots to capture on schedule change. `null`/`0` = none. |
| `screenshotBurstIntervalSeconds` | `int?` | Seconds between burst screenshots. |

---

### POST /api/device/{deviceId}/logs

Upload a batch of log entries from the player. Capped at 500 entries per batch.

**Auth:** `X-Api-Key` header (required; device must be registered).

**Request body** (`DeviceLogBatchDto`):
```json
{
  "entries": [
    {
      "level": "Warning",
      "categoryName": "PlayerWorker",
      "message": "Content file not found in cache",
      "exception": null,
      "timestamp": "2026-06-17T03:01:00Z"
    }
  ]
}
```

**Response:** `200 OK` (also returns 200 for empty batches).

---

### POST /api/device/{deviceId}/screenshot/notify

Player notifies the server that a screenshot has been written to the SMB share.

**Auth:** `X-Api-Key` header (required).

**Request:** `multipart/form-data` with field `fileName` (string, the filename on the share).

**Response:** `200 OK` on success. `404` if the file is not found on the share. `400` for missing/invalid `fileName`.

---

## Media Serving

Serve files directly from the SMB share. All routes require authentication (admin user). Responses include `Cache-Control: no-cache`.

### GET /api/media/content/{fileName}

Serve a content file from the `ContentPath` share. Supports byte-range requests.

### GET /api/media/idle/{fileName}

Serve an idle/fallback file from the `IdlePath` share.

### GET /api/media/branding/{fileName}

Serve a branding asset from the `BrandingPath` share.

### GET /api/media/screenshot/{deviceId}/{fileName}

Serve a screenshot PNG from `ScreenshotsPath/{deviceId}/`.

---

## Content Transforms

Server-side media processing. All routes require authentication (admin user).

### POST /api/content/{id:int}/image-to-video

Convert a static image into a looping MP4. Creates a new `ContentItem` linked via `SourceContentItemId`.

**Query parameter:** `durationSeconds` (int, 1–3600).

**Response** (200):
```json
{ "id": 42, "fileName": "slide_loop.mp4" }
```

---

### POST /api/content/{id:int}/extract-frame

Extract a frame from a video and save as a new image content item.

**Query parameter:** `position` (string, `"first"` or `"last"`).

**Response** (200):
```json
{ "id": 43, "fileName": "worship_frame_first.jpg" }
```

---

### POST /api/content/{id:int}/ken-burns

Generate a Ken Burns zoom-pan video from an image with optional overlay with parallax. Creates a new `ContentItem` linked via `SourceContentItemId`.

**Request body** (`KenBurnsRequest`):
```json
{
  "startX": 0.1,
  "startY": 0.0,
  "startW": 0.8,
  "endX": 0.0,
  "endY": 0.1,
  "endW": 1.0,
  "durationSeconds": 10,
  "overlayContentItemId": null,
  "overlayParallaxRate": 0.0
}
```

All rect values (`startX`/`startY`/`startW`/`endX`/`endY`/`endW`) are normalized 0.0–1.0 within the image, with the constraint `x + w <= 1` and `y` within image bounds. `durationSeconds` is 1–3600. `overlayParallaxRate` controls the parallax depth effect when an overlay is present.

**Response** (200):
```json
{ "id": 44, "fileName": "slide_kenburns_20260617120000.mp4" }
```

---

## Updates

Publicly accessible (no auth) — used by installer scripts and players.

### GET /api/updates/latest/binary

Download the latest published player binary (application/octet-stream). Returns the most recently uploaded version.

### GET /api/updates/{version}/binary

Download a specific player binary by version string (e.g. `1.20.0`).

---

## Health Check

### GET /health

Standard ASP.NET health check endpoint. Returns `200 Healthy` when the server and database are reachable.

---

## Data Models

### DeviceTelemetryDto
```csharp
public class DeviceTelemetryDto
{
    public string DeviceId { get; set; }
    public string AppVersion { get; set; }
    public string? CurrentFileName { get; set; }
    public string? CallbackUrl { get; set; }
    public DateTime Timestamp { get; set; }
}
```

### DeviceLogBatchDto
```csharp
public class DeviceLogBatchDto
{
    public List<DeviceLogEntryDto> Entries { get; set; }
}

public class DeviceLogEntryDto
{
    public string Level { get; set; }
    public string? CategoryName { get; set; }
    public string Message { get; set; }
    public string? Exception { get; set; }
    public DateTime Timestamp { get; set; }
}
```

### HeartbeatResponse
```csharp
public class HeartbeatResponse
{
    public bool RequestScreenshot { get; set; }
    public string? UpdateFileName { get; set; }
    public string? ExpectedSha256 { get; set; }
    public string? AssignedApiKey { get; set; }
    public List<PlaylistItemDto>? PlaylistItems { get; set; }
    public PlaylistItemDto? BrandingItem { get; set; }
    public bool LiveMode { get; set; }
    public string? NdiSourceName { get; set; }
    public int? LogMinLevel { get; set; }
    public int? LogShipIntervalSeconds { get; set; }
    public int? ScreenshotBurstCount { get; set; }
    public int? ScreenshotBurstIntervalSeconds { get; set; }
}
```

### PlaylistItemDto
```csharp
public class PlaylistItemDto
{
    public string FileName { get; set; }
    public int DurationSeconds { get; set; }
}
```

### KenBurnsRequest
```csharp
public sealed record KenBurnsRequest(
    double StartX, double StartY, double StartW,
    double EndX, double EndY, double EndW,
    int DurationSeconds,
    int? OverlayContentItemId,
    double OverlayParallaxRate);
```

---

## Error Responses

Standard ASP.NET `ProblemDetails` format (`application/problem+json`):

```json
{
  "type": "https://tools.ietf.org/html/rfc7235#section-3.1",
  "title": "Missing X-Api-Key header",
  "status": 401
}
```
