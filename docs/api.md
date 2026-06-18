# PlainSight API Documentation

REST API reference for the PlainSight server. All routes requiring authentication use cookie-based auth (admin UI login). Content management (upload, delete, rename, playlists, schedules) is performed server-side via Blazor — these routes are not part of the REST surface.

## Base URL

```
http://localhost:8080/api
```

---

## Device API

### POST /api/device/heartbeat

Player telemetry. Creates or upserts the device record and returns pending commands.

**Request body**:
```json
{
  "deviceId": "plainsight-sanctuary",
  "appVersion": "1.18.0",
  "currentFileName": "worship.mp4",
  "timestamp": "2026-06-17T03:00:00Z"
}
```

**Response**:
```json
{
  "requestScreenshot": false,
  "updateUrl": "/api/updates/1.19.0/binary",
  "scheduleChanged": false
}
```

`updateUrl` is `null` when no update is pending. `requestScreenshot` clears after the player calls `screenshot/notify`.

---

### POST /api/device/{deviceId}/logs

Upload a log batch from the player. Body is plain text (newline-separated log lines). Returns `204 No Content`.

---

### POST /api/device/{deviceId}/screenshot/notify

Player calls this after writing a PNG to `/mnt/plainsight/screenshots/{deviceId}/`. Body:
```json
{ "fileName": "screenshot_1718589600.png" }
```
Returns `200 OK`. Server verifies the file exists on the share and adds it to the screenshot history.

---

## Media Serving

Serves files directly from the SMB share. No auth required (served to players).

### GET /api/media/content/{fileName}

Serve a content file from `/mnt/plainsight/content/`. Supports byte-range requests for streaming.

### GET /api/media/idle/{fileName}

Serve an idle/fallback loop file from `/mnt/plainsight/idle/`.

### GET /api/media/branding/{fileName}

Serve a branding asset from `/mnt/plainsight/branding/`.

### GET /api/media/screenshot/{deviceId}/{fileName}

Serve a screenshot from `/mnt/plainsight/screenshots/{deviceId}/`.

---

## Content Transforms

Server-side media processing. All routes require authentication.

### POST /api/content/{id}/image-to-video

Convert a static image content item into a looping MP4. Creates a new `ContentItem` linked via `SourceContentItemId`.

**Request body**:
```json
{ "durationSeconds": 10 }
```

**Response**:
```json
{ "id": 42, "fileName": "slide_loop.mp4" }
```

---

### POST /api/content/{id}/extract-frame

Extract a frame from a video and save as a new image content item.

**Request body**:
```json
{ "position": "first" }
```
`position`: `"first"` or `"last"`.

**Response**:
```json
{ "id": 43, "fileName": "worship_frame.jpg" }
```

---

### POST /api/content/{id}/ken-burns

Generate a Ken Burns zoom-pan video from an image. Creates a new `ContentItem`.

**Request body**:
```json
{
  "startX": 0,
  "startY": 0,
  "startW": 100,
  "endX": 10,
  "endY": 10,
  "endW": 80,
  "durationSeconds": 10,
  "overlayContentItemId": null,
  "parallaxRate": 0.0,
  "outputFileName": "slide_kenburns"
}
```

**Response**:
```json
{ "id": 44, "fileName": "slide_kenburns.mp4" }
```

---

## Updates

### GET /api/updates/latest/binary

Download the latest published player binary (application/octet-stream). Used by players when `updateUrl` is not yet known.

### GET /api/updates/{version}/binary

Download a specific player binary by version string (e.g. `1.19.0`). This is the URL returned in heartbeat responses.

---

## Health Check

### GET /health

Standard ASP.NET health endpoint. Returns `200 Healthy` when the server and database are reachable.

---

## Data Models

### DeviceTelemetryDto
```csharp
public class DeviceTelemetryDto
{
    public string DeviceId { get; init; }
    public string AppVersion { get; init; }
    public string? CurrentFileName { get; init; }
    public DateTime Timestamp { get; init; }
}
```

### HeartbeatResponse
```csharp
public class HeartbeatResponse
{
    public bool RequestScreenshot { get; init; }
    public string? UpdateUrl { get; init; }
    public bool ScheduleChanged { get; init; }
}
```

### ContentItem (key fields)
```csharp
public class ContentItem
{
    public int Id { get; init; }
    public string Name { get; set; }
    public string FileName { get; set; }          // unique on disk
    public ContentType Type { get; set; }          // Video | Image
    public long FileSizeBytes { get; set; }
    public int DurationSeconds { get; set; }
    public string? ThumbnailFileName { get; set; } // sidecar _thumb.jpg
    public int? SourceContentItemId { get; set; }  // original item this was derived from
    public int? CompanionContentItemId { get; set; }
    public CompanionPosition? CompanionPosition { get; set; } // Before | After
}
```

---

## Error Responses

Standard ASP.NET `ProblemDetails` format:

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4",
  "title": "Not Found",
  "status": 404
}
```
