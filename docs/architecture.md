# PlainSight Architecture

This document describes the technical architecture of the PlainSight digital signage system.

## System Overview

PlainSight uses a distributed architecture with server-side rendering to ensure reliable 24/7 operation of digital signage displays.

```
┌─────────────────────────────────────────────────────────┐
│                    GitHub Actions                        │
│  ┌──────────────────┐      ┌──────────────────┐        │
│  │  Build Server    │      │  Build Player    │        │
│  │  Docker Image    │      │  ARM64 Binary    │        │
│  └────────┬─────────┘      └────────┬─────────┘        │
│           │                         │                   │
└───────────┼─────────────────────────┼───────────────────┘
            │                         │
            ▼                         ▼
┌─────────────────────────────────────────────────────────┐
│              Production Server (macOS)                   │
│  ┌──────────────────────────────────────────────────┐  │
│  │              Docker Compose                       │  │
│  │  ┌────────────┐  ┌────────────┐  ┌────────────┐ │  │
│  │  │ PostgreSQL │  │  Signage   │  │   Samba    │ │  │
│  │  │  Database  │  │   Server   │  │File Share  │ │  │
│  │  └────────────┘  └────────────┘  └────────────┘ │  │
│  └──────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
            │                         │
            │ Heartbeat API           │ SMB Stream
            │ Updates                 │ Content Files
            ▼                         ▼
┌─────────────────────────────────────────────────────────┐
│              Raspberry Pi 5 Players                      │
│  ┌────────────────┐  ┌────────────────┐  ┌───────────┐ │
│  │   Heartbeat    │  │   Self-Update  │  │Screenshot │ │
│  │    Service     │  │    Service     │  │  Service  │ │
│  └────────────────┘  └────────────────┘  └───────────┘ │
│                   Player Application                     │
└─────────────────────────────────────────────────────────┘
```

## Components

### 1. Signage.Server (Admin Application)

**Technology**: ASP.NET Core 10, Blazor Web App

**Responsibilities**:
- Device fleet management
- Content rendering (HTML to video)
- Update distribution
- Telemetry collection
- Configuration management

**Key Services**:

#### WebsiteRecorder
Converts websites to video using PuppeteerSharp:
```csharp
public class WebsiteRecorder
{
    public async Task<string> ConvertUrlToVideoAsync(
        string url, int durationSec, string outputPath)
    {
        // Launch headless Chrome
        // Set viewport (1080p or 4K)
        // Capture frames
        // Encode to H.264/MP4
    }
}
```

#### DeviceController
Handles device communication:
```csharp
[HttpPost("heartbeat")]
public async Task<IActionResult> Heartbeat(DeviceTelemetryDto data)
{
    // Update device status
    // Check for updates
    // Handle screenshot requests
}
```

#### VersionService
Manages update distribution:
```csharp
public class VersionService
{
    public string GetTargetVersion(string deviceGroup)
    {
        // Returns target version for device group
        // Enables canary deployments
    }
}
```

### 2. Signage.Player (Raspberry Pi Client)

**Technology**: .NET 10 Console Application

**Responsibilities**:
- Stream content from SMB share
- Report telemetry to server
- Self-update when new versions available
- Capture screenshots on demand

**Key Services**:

#### HeartbeatService
Communicates with server:
```csharp
public class HeartbeatService
{
    public async Task<HeartbeatResponse> SendHeartbeat(
        string currentFile)
    {
        // Send device status
        // Receive commands (update, screenshot)
    }
}
```

#### UpdateService
Handles self-updates:
```csharp
public class UpdateService
{
    public async Task PerformSelfUpdate(string updateUrl)
    {
        // Download new binary
        // Swap files
        // Exit (systemd restarts)
    }
}
```

#### ScreenCaptureService
Captures display output:
```csharp
public class ScreenCaptureService
{
    public async Task<byte[]> CaptureScreenshot()
    {
        // Use grim to capture Wayland framebuffer
        // Return PNG data
    }
}
```

### 3. Database (PostgreSQL)

**Schema**:

```sql
CREATE TABLE Devices (
    Id SERIAL PRIMARY KEY,
    DeviceId VARCHAR(255) UNIQUE NOT NULL,
    Name VARCHAR(255) NOT NULL,
    "Group" VARCHAR(255) DEFAULT 'Default',
    LastSeen TIMESTAMP NOT NULL,
    CurrentVersion VARCHAR(50) NOT NULL,
    CurrentlyPlaying VARCHAR(255),
    ScreenshotRequested BOOLEAN DEFAULT FALSE
);
```

### 4. File Share (Samba)

**Purpose**: 
- Distribute rendered content to players
- SMB protocol for compatibility
- Read-only access for players

**Structure**:
```
/share
  ├── content/
  │   ├── video1.mp4
  │   ├── video2.mp4
  │   └── playlist.json
  └── updates/
      └── 1.0.0/
          └── Signage.Player
```

### 5. Orchestration (.NET Aspire)

**Purpose**:
- Service discovery
- Configuration management
- Development-time orchestration

**Configuration**:
```csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres");
var signageDb = postgres.AddDatabase("signagedb");

builder.AddProject<Projects.Signage_Server>("signage-server")
    .WithReference(signageDb);
```

## Communication Protocols

### 1. HTTP/REST API

**Heartbeat Endpoint**:
```http
POST /api/device/heartbeat
Content-Type: application/json

{
  "deviceId": "plainsight-sanctuary",
  "appVersion": "1.0.0",
  "currentFileName": "worship-service.mp4",
  "timestamp": "2026-01-25T22:00:00Z"
}
```

**Response**:
```json
{
  "requestScreenshot": false,
  "updateUrl": "/api/updates/1.1.0/binary"
}
```

### 2. SMB File Sharing

**Mount Point**: `/mnt/signage`
**Protocol**: CIFS/SMB 3.0
**Access**: Read-only for players

### 3. Systemd Automount

**Benefits**:
- Non-blocking boot
- Automatic retry on network issues
- Lazy mounting

## Data Flow

### Device Registration Flow

```
1. Player boots → 2. Send heartbeat → 3. Server creates device record
                                    ↓
4. Server responds ← 5. Device ID stored ← 6. Continue heartbeat
```

### Update Flow

```
1. New version tagged in GitHub
2. GitHub Actions builds ARM64 binary
3. Binary uploaded to server /api/updates/{version}
4. Admin assigns version to device group
5. Player heartbeat detects update available
6. Player downloads and installs update
7. Player restarts with new version
```

### Screenshot Flow

```
1. Admin requests screenshot
2. Server sets ScreenshotRequested flag
3. Player detects flag in heartbeat response
4. Player captures screen using grim
5. Player uploads PNG to server
6. Admin views screenshot in dashboard
```

## Deployment Architecture

### Development Environment

```
Developer Workstation
  ├── .NET 10 SDK
  ├── Docker Desktop
  └── .NET Aspire
       ├── Signage.Server (localhost:5000)
       └── PostgreSQL (localhost:5432)
```

### Production Environment

```
macOS Server (Docker Desktop)
  ├── Docker Compose
  │   ├── PostgreSQL Container
  │   ├── Signage.Server Container
  │   └── Samba Container
  └── GitHub Actions Runner (Self-hosted)

Raspberry Pi Fleet (Distributed)
  ├── Device 1 (Sanctuary)
  ├── Device 2 (Lobby)
  ├── Device 3 (Chapel)
  └── Device N...
```

## Security Architecture

### Authentication & Authorization

- **Admin Web**: Cookie-based authentication (future)
- **API**: Device ID-based (no authentication currently)
- **SMB**: Username/password (pi/secure)

### Network Security

- **Firewall**: Restrict PostgreSQL port (5432) to localhost
- **SMB**: Read-only access for players
- **HTTPS**: Recommended for production (reverse proxy)

### Update Security

- **Binary Verification**: SHA256 checksums (future enhancement)
- **Rollback**: Previous binary kept as .bak
- **Canary**: Test on subset before fleet-wide

## Scalability

### Horizontal Scaling

**Server**:
- Run multiple server instances behind load balancer
- Shared PostgreSQL database
- Shared file storage (NFS/SMB)

**Database**:
- PostgreSQL replication for read scaling
- Connection pooling

### Device Limits

- Designed for 10-100 devices per server
- Each device: ~1KB/heartbeat every 30s
- Network bandwidth: ~10Mbps per 4K stream

## Monitoring & Observability

### Metrics

- Device online/offline status
- Heartbeat frequency
- Update success/failure rate
- Screenshot capture latency

### Logging

**Server**:
- ASP.NET Core logging
- Entity Framework query logging
- PuppeteerSharp browser logs

**Player**:
- Systemd journal
- Player application logs

### Health Checks

- `/health` endpoint
- Database connectivity check
- File share availability

## Performance Considerations

### Server

- **PuppeteerSharp**: CPU-intensive rendering
- **Database**: Indexed queries on DeviceId
- **File I/O**: SSD recommended for video storage

### Player

- **Video Decoding**: Hardware-accelerated (VideoCore VII)
- **Memory**: 4GB sufficient for single stream
- **Network**: Gigabit required for 4K streaming

## Disaster Recovery

### Backup Strategy

- **Database**: Daily pg_dump backups
- **Content**: File share snapshots
- **Configuration**: Version controlled in Git

### Recovery Procedures

- **Server Failure**: Restore from Docker image
- **Database Failure**: Restore from backup
- **Player Failure**: Re-run install script

## Future Enhancements

- [ ] Authentication & authorization
- [ ] Multi-tenancy support
- [ ] Advanced content scheduling
- [ ] Analytics and reporting
- [ ] Mobile app for monitoring
- [ ] WebRTC for live camera feeds
- [ ] Content CDN integration

## References

- [ASP.NET Core Documentation](https://docs.microsoft.com/aspnet/core)
- [.NET Aspire Documentation](https://learn.microsoft.com/dotnet/aspire)
- [PuppeteerSharp Documentation](https://www.puppeteersharp.com/)
- [Raspberry Pi Documentation](https://www.raspberrypi.com/documentation/)
