# PlainSight API Documentation

REST API reference for the PlainSight server.

## Base URL

```
http://localhost:8080/api
```

## Endpoints

### Device Management

#### Send Heartbeat

Report device telemetry and receive commands.

**Endpoint**: `POST /device/heartbeat`

**Request Body**:
```json
{
  "deviceId": "string",
  "appVersion": "string",
  "currentFileName": "string (optional)",
  "timestamp": "datetime"
}
```

**Response**:
```json
{
  "requestScreenshot": boolean,
  "updateUrl": "string (optional)"
}
```

**Example**:
```bash
curl -X POST http://localhost:8080/api/device/heartbeat \
  -H "Content-Type: application/json" \
  -d '{
    "deviceId": "plainsight-sanctuary",
    "appVersion": "1.0.0",
    "currentFileName": "worship.mp4",
    "timestamp": "2026-01-25T22:00:00Z"
  }'
```

#### Get All Devices

Retrieve list of all registered devices.

**Endpoint**: `GET /device`

**Response**:
```json
[
  {
    "id": 1,
    "deviceId": "plainsight-sanctuary",
    "name": "Sanctuary Display",
    "group": "Default",
    "lastSeen": "2026-01-25T22:00:00Z",
    "currentVersion": "1.0.0",
    "currentlyPlaying": "worship.mp4",
    "screenshotRequested": false
  }
]
```

**Example**:
```bash
curl http://localhost:8080/api/device
```

#### Request Screenshot

Request a screenshot from a specific device.

**Endpoint**: `POST /device/{deviceId}/screenshot`

**Parameters**:
- `deviceId` (path): Device identifier

**Response**: `200 OK` on success

**Example**:
```bash
curl -X POST http://localhost:8080/api/device/plainsight-sanctuary/screenshot
```

### Health Check

#### Server Health

Check if the server is running and healthy.

**Endpoint**: `GET /health`

**Response**: `200 OK` if healthy

**Example**:
```bash
curl http://localhost:8080/health
```

## Data Models

### DeviceTelemetryDto

```csharp
public class DeviceTelemetryDto
{
    public string DeviceId { get; set; }
    public string AppVersion { get; set; }
    public string? CurrentFileName { get; set; }
    public DateTime Timestamp { get; set; }
}
```

### HeartbeatResponse

```csharp
public class HeartbeatResponse
{
    public bool RequestScreenshot { get; set; }
    public string? UpdateUrl { get; set; }
}
```

### Device

```csharp
public class Device
{
    public int Id { get; set; }
    public string DeviceId { get; set; }
    public string Name { get; set; }
    public string Group { get; set; }
    public DateTime LastSeen { get; set; }
    public string CurrentVersion { get; set; }
    public string? CurrentlyPlaying { get; set; }
    public bool ScreenshotRequested { get; set; }
}
```

## Error Responses

### 400 Bad Request

Invalid request data.

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "DeviceId": ["The DeviceId field is required."]
  }
}
```

### 404 Not Found

Resource not found.

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4",
  "title": "Not Found",
  "status": 404
}
```

### 500 Internal Server Error

Server error occurred.

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.6.1",
  "title": "An error occurred while processing your request.",
  "status": 500
}
```

## Rate Limiting

Currently no rate limiting is implemented. Future versions may add:
- 100 requests per minute per device
- 1000 requests per minute per IP

## Authentication

Currently no authentication is required. Future versions will add:
- API key authentication for devices
- OAuth 2.0 for admin access

## Versioning

API version is not currently enforced in the URL. Future versions may use:
- URL versioning: `/api/v1/device/heartbeat`
- Header versioning: `API-Version: 1.0`

## WebSocket Support (Future)

Planned for real-time updates:
- `/ws/device/{deviceId}` - Real-time device updates
- `/ws/admin` - Admin dashboard updates

## Examples

### Device Registration Flow

```bash
# 1. Send initial heartbeat (creates device)
curl -X POST http://localhost:8080/api/device/heartbeat \
  -H "Content-Type: application/json" \
  -d '{
    "deviceId": "plainsight-new",
    "appVersion": "1.0.0",
    "currentFileName": null,
    "timestamp": "2026-01-25T22:00:00Z"
  }'

# Response: {"requestScreenshot":false,"updateUrl":null}

# 2. Verify device created
curl http://localhost:8080/api/device

# 3. Continue sending heartbeats every 30 seconds
```

### Update Flow

```bash
# 1. Check current version
curl http://localhost:8080/api/device | jq '.[] | select(.deviceId=="plainsight-sanctuary")'

# 2. Server admin assigns new version to device group

# 3. Next heartbeat returns update URL
curl -X POST http://localhost:8080/api/device/heartbeat \
  -H "Content-Type: application/json" \
  -d '{
    "deviceId": "plainsight-sanctuary",
    "appVersion": "1.0.0",
    "currentFileName": "worship.mp4",
    "timestamp": "2026-01-25T22:00:00Z"
  }'

# Response: {"requestScreenshot":false,"updateUrl":"/api/updates/1.1.0/binary"}

# 4. Device downloads and applies update
```

### Screenshot Flow

```bash
# 1. Admin requests screenshot
curl -X POST http://localhost:8080/api/device/plainsight-sanctuary/screenshot

# 2. Next heartbeat detects screenshot request
curl -X POST http://localhost:8080/api/device/heartbeat \
  -H "Content-Type: application/json" \
  -d '{
    "deviceId": "plainsight-sanctuary",
    "appVersion": "1.0.0",
    "currentFileName": "worship.mp4",
    "timestamp": "2026-01-25T22:00:00Z"
  }'

# Response: {"requestScreenshot":true,"updateUrl":null}

# 3. Device captures and uploads screenshot (endpoint TBD)
```

## Testing

### Using curl

```bash
# Test heartbeat
curl -X POST http://localhost:8080/api/device/heartbeat \
  -H "Content-Type: application/json" \
  -d '{"deviceId":"test","appVersion":"1.0.0","timestamp":"2026-01-25T22:00:00Z"}'

# Test get devices
curl http://localhost:8080/api/device

# Test screenshot request
curl -X POST http://localhost:8080/api/device/test/screenshot
```

### Using PowerShell

```powershell
# Test heartbeat
$body = @{
    deviceId = "test"
    appVersion = "1.0.0"
    timestamp = "2026-01-25T22:00:00Z"
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:8080/api/device/heartbeat" `
    -Method Post -Body $body -ContentType "application/json"

# Test get devices
Invoke-RestMethod -Uri "http://localhost:8080/api/device"
```

## SDK (Future)

Planned client SDKs:
- .NET Client Library
- Python Client Library
- JavaScript/TypeScript Client Library

## Support

For API questions or issues:
- Open a GitHub issue
- Check the [Architecture Documentation](architecture.md)
- Review the [source code](../src/PlainSight.Server/Controllers/)
