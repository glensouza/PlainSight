# Configuration Reference

Every configuration key consumed by the PlainSight server and player, derived from the source code. Keys can be set in `appsettings.json`, environment variables (ASP.NET `__` convention for nested keys, e.g. `Alerts__Email__SmtpHost`), or command-line arguments.

---

## Server Configuration

### Storage Paths

Keys shared with the player (point at the same SMB mount).

| Key | Type | Default | Consumer(s) | Description |
|---|---|---|---|---|
| `ContentPath` | `string` | `/mnt/plainsight/content` | `ContentApi`, `TransformApi`, `ContentSyncService`, `YouTubeDownloadService`, `WatermarkVideoWorkerService`, `SvdGenerationService`, `RenderWorkerService`, `Program` | Root directory for rendered/uploaded/downloaded media files. |
| `IdlePath` | `string` | `/mnt/plainsight/idle` | `ContentApi`, `Program` | Directory for idle/fallback loop files shown when no schedule is active. |
| `BrandingPath` | `string` | `/mnt/plainsight/branding` | `ContentApi`, `BrandingSyncService`, `Program` | Directory for branding overlay video clips. |
| `ScreenshotsPath` | `string` | `/mnt/plainsight/screenshots` | `ContentApi`, `DeviceApi`, `Program` | Directory where player screenshots are stored on the SMB share. |
| `UpdatesPath` | `string` | `/mnt/plainsight/updates` | `UpdateApi`, `ManifestReconciler`, `Program` | Directory for player update binaries and version manifests. |

### NDI Discovery / Live Mode

Required for live video switching over NDI.

| Key | Type | Default | Consumer(s) | Description |
|---|---|---|---|---|
| `Ndi:ScanIntervalSeconds` | `int` | `15` | `NdiDiscoveryService` | How often the server scans for NDI sources via mDNS. |
| `Ndi:ScanTimeoutSeconds` | `int` | `5` | `NdiDiscoveryService` | Timeout for each mDNS scan attempt. |
| `Ndi:StalenessSeconds` | `int` | `60` | `DeviceApi`, `NdiDiscoveryService` | Seconds before an NDI source is considered offline. Used in heartbeat auto-live-mode decisions. |

### OBS WebSocket Integration

Optional — enables auto-live-mode when OBS NDI Output is active.

| Key | Type | Default | Consumer(s) | Description |
|---|---|---|---|---|
| `OBS:WebSocketUrl` | `string?` | `null` | `ObsDiscoveryService` | OBS WebSocket URL (e.g. `ws://192.168.1.50:4455`). `null`/empty disables OBS integration. |
| `OBS:WebSocketPassword` | `string?` | `null` | `ObsDiscoveryService` | OBS WebSocket v5 authentication password. |
| `OBS:NdiSourceName` | `string?` | `null` | `ObsDiscoveryService` | Exact NDI source name to mark as live when OBS output is active. Must match an NDI Sources entry. |
| `OBS:NdiOutputName` | `string?` | `null` | `ObsDiscoveryService` | Override for auto-detected NDI output name in OBS. |
| `OBS:SyncWithStreaming` | `bool` | `true` | `ObsDiscoveryService` | Treat OBS streaming as a live event (forces live mode on heartbeat). |
| `OBS:SyncWithRecording` | `bool` | `true` | `ObsDiscoveryService` | Treat OBS recording as a live event (forces live mode on heartbeat). |

### YouTube Download

| Key | Type | Default | Consumer(s) | Description |
|---|---|---|---|---|
| `YouTube:MaxDownloadBytes` | `long` | `2147483648` (2 GB) | `YouTubeDownloadService` | Maximum download size in bytes. |
| `YouTube:MaxDurationSeconds` | `int` | `7200` (2 hours) | `YouTubeDownloadService` | Maximum video duration in seconds. |
| `YouTube:Shrink:Enabled` | `bool` | `true` | `YouTubeDownloadService` | Re-encode downloaded videos via ffmpeg. |
| `YouTube:Shrink:MaxHeight` | `int` | `1080` | `YouTubeDownloadService` | Scale-down target height in pixels. |
| `YouTube:Shrink:Crf` | `int` | `28` | `YouTubeDownloadService` | Constant Rate Factor (higher = smaller file, lower quality). |
| `YouTube:Shrink:Preset` | `string` | `medium` | `YouTubeDownloadService` | ffmpeg encoding preset (`fast`, `medium`, `slow`, etc.). |
| `YouTube:Shrink:AudioBitrate` | `string` | `128k` | `YouTubeDownloadService` | Audio bitrate for ffmpeg `-b:a`. |

### Logging

| Key | Type | Default | Consumer(s) | Description |
|---|---|---|---|---|
| `Logging:PlayerMinLevel` | `int` | `3` (Warning) | `DeviceApi` | Minimum `LogLevel` server tells players to ship. Sent in heartbeat response. |
| `Logging:PlayerShipIntervalSeconds` | `int` | `60` | `DeviceApi` | Interval server tells players to flush buffered logs. Sent in heartbeat response. |
| `Logging:RetentionDays` | `int` | `30` | `LogRetentionService` | Days to keep log entries in the database before pruning. |
| `DbLogger:MinimumLevel` | `string` | `Warning` | `DbLoggerProvider` | Minimum log level for entries persisted to the database. |

### Screenshots

| Key | Type | Default | Consumer(s) | Description |
|---|---|---|---|---|
| `ScreenshotHistoryLimit` | `int` | `10` | `DeviceApi` | Max screenshot history records per device. |
| `ScreenshotRetentionDays` | `int` | `7` | `DeviceApi` | Days to retain screenshot files before deletion. |
| `ScreenshotIntervalMinutes` | `int` | `15` | `AutoScreenshotService` | Interval for automatic screenshot requests to all online devices. Minimum 1. |

### Scheduling

| Key | Type | Default | Consumer(s) | Description |
|---|---|---|---|---|
| `Schedules:CacheSeconds` | `int` | `15` | `ScheduleCache` | TTL in seconds for the in-memory schedule cache. 0 or negative disables caching. |

### Player Version Reconciliation

| Key | Type | Default | Consumer(s) | Description |
|---|---|---|---|---|
| `PlayerVersions:ReconcileEnabled` | `bool` | `true` | `ReconciliationBackgroundService` | Enable/disable automatic manifest ingestion from the Updates folder. |
| `PlayerVersions:ReconcileIntervalSeconds` | `double` | `60.0` | `ReconciliationBackgroundService` | Scan interval for new player version manifests. |

### System

| Key | Type | Default | Consumer(s) | Description |
|---|---|---|---|---|
| `SystemTimeZone` | `string?` | `null` (OS local) | `TimeExtensions` | IANA time zone ID used for schedule evaluation. Falls back to `TimeZoneInfo.Local` if unset or invalid. |
| `PublicKeyPath` | `string` | `AppContext.BaseDirectory/Keys/release-signing.pub` | `Program`, `SignatureVerifier` | Filesystem path to the ECDSA (P-256) PEM public key for version manifest signature verification. |
| `ConnectionStrings:plainsightdb` | `string` | _(required)_ | `Program` | PostgreSQL connection string for Entity Framework Core. |

### SVD (Stable Video Diffusion)

Optional — enables image-to-video animation via ComfyUI. **All SVD is disabled if `Svd:ComfyUiBaseUrl` is null or empty.**

| Key | Type | Default | Consumer(s) | Description |
|---|---|---|---|---|
| `Svd:ComfyUiBaseUrl` | `string?` | `null` | `SvdGenerationService` | ComfyUI server base URL (e.g. `http://comfyui:8188`). Null/empty disables SVD. |
| `Svd:RequestTimeoutSeconds` | `int` | `600` | `SvdGenerationService` | Maximum time per generation job before timeout. |
| `Svd:Model` | `string` | `svd_xt.safetensors` | `SvdGenerationService` | SVD model checkpoint filename known to ComfyUI. |
| `Svd:VideoFrames` | `int` | `25` | `SvdGenerationService` | Number of video frames to generate. |
| `Svd:Fps` | `int` | `6` | `SvdGenerationService` | Frames per second for generated video. |
| `Svd:MotionBucketId` | `int` | `127` | `SvdGenerationService` | Motion bucket ID controlling motion amount. |
| `Svd:Steps` | `int` | `20` | `SvdGenerationService` | Number of sampling steps. |
| `Svd:Cfg` | `double` | `2.5` | `SvdGenerationService` | Classifier-free guidance scale. |
| `Svd:OutputWidth` | `int` | `1024` | `SvdGenerationService` | Output video width in pixels. |
| `Svd:OutputHeight` | `int` | `576` | `SvdGenerationService` | Output video height in pixels. |
| `Svd:FilenamePrefix` | `string` | `plainsight_svd` | `SvdGenerationService` | Prefix for generated video filenames. |

### Device Alerts (Email)

Optional — sends email when devices go offline. Bound via `AlertsOptions` / `AlertEmailOptions`. Alerting is disabled if `Alerts:Enabled` is `false` or SMTP settings are missing.

| Key | Type | Default | Consumer(s) | Description |
|---|---|---|---|---|
| `Alerts:Enabled` | `bool` | `true` | `DeviceMonitorService` | Master switch for device offline alerting. |
| `Alerts:OfflineThresholdMinutes` | `int` | `5` | `DeviceMonitorService` | Minutes a device must be unseen before an offline alert is sent. |
| `Alerts:Email:To` | `string` | `""` | `DeviceMonitorService` | Recipient email address. |
| `Alerts:Email:From` | `string` | `""` | `DeviceMonitorService` | From address (falls back to `Username` if empty). |
| `Alerts:Email:SmtpHost` | `string` | `""` | `DeviceMonitorService` | SMTP server hostname. |
| `Alerts:Email:SmtpPort` | `int` | `587` | `DeviceMonitorService` | SMTP server port. |
| `Alerts:Email:Username` | `string` | `""` | `DeviceMonitorService` | SMTP username (and default From address). |
| `Alerts:Email:Password` | `string` | `""` | `DeviceMonitorService` | SMTP password. |

---

## Player Configuration

### Storage Paths

Local cache directories. Shared path keys (`ContentPath`, `IdlePath`, `BrandingPath`) are also read by the player for SMB access.

| Key | Type | Default | Consumer(s) | Description |
|---|---|---|---|---|
| `ContentPath` | `string` | `/mnt/plainsight/content` | `Program` | SMB mount point for content files. |
| `CachePath` | `string` | `/var/cache/plainsight/content` | `Program` | Local cache directory for content copied from SMB. |
| `IdlePath` | `string` | `/mnt/plainsight/idle` | `Program` | SMB mount point for idle/fallback files. |
| `IdleCachePath` | `string` | `/var/cache/plainsight/idle` | `Program` | Local cache for idle files. |
| `BrandingPath` | `string` | `/mnt/plainsight/branding` | `Program` | SMB mount point for branding files. |
| `BrandingCachePath` | `string` | `/var/cache/plainsight/branding` | `Program` | Local cache for branding files. |

### Core

| Key | Type | Default | Consumer(s) | Description |
|---|---|---|---|---|
| `ServerUrl` | `string` | `http://plainsight-server` | `Program` | Base URL of the PlainSight server for heartbeat, updates, and log shipping. |
| `SplashPath` | `string` | `/opt/plainsight/splash.png` | `Program`, `SplashGeneratorService` | Path where the boot splash screen PNG is generated/served. |

### NDI Viewer

| Key | Type | Default | Consumer(s) | Description |
|---|---|---|---|---|
| `NdiViewerPath` | `string` | `dicaffeine` | `NdiPlayerService` | Path to the NDI viewer executable launched for live mode. |
| `NdiViewerArgs` | `string` | `--fullscreen --source "{0}"` | `NdiPlayerService` | Command-line argument template. `{0}` is replaced with the NDI source name. |

### Telemetry

| Key | Type | Default | Consumer(s) | Description |
|---|---|---|---|---|
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | `string?` | `null` | `Program` | Application Insights connection string (OpenTelemetry). |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `string?` | `null` | `Program` | OpenTelemetry OTLP exporter endpoint. |

---

## Environment Variable Convention

ASP.NET Core translates nested config keys to environment variables using double-underscore separators. Example:

```
# appsettings.json nested form:
{ "OBS": { "WebSocketUrl": "ws://192.168.1.50:4455" } }

# Equivalent environment variable:
OBS__WebSocketUrl=ws://192.168.1.50:4455
```

Single-level keys are set directly (e.g. `ContentPath=/mnt/plainsight/content`).
