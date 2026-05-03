# NDI Livestream Integration

PlainSight can switch player TVs from cached signage to a live NDI feed (e.g. an OBS service stream)
based on whether the configured NDI source is currently broadcasting on the local network.

## OBS configuration

1. Install the **OBS-NDI** plugin (https://github.com/obs-ndi/obs-ndi/releases) on the streaming PC.
2. In OBS, open **Tools → NDI Output Settings**.
3. Enable **Main Output** and give it a recognizable name, e.g. `Sanctuary-Livestream`.
4. Click OK. OBS now advertises an NDI source over mDNS (`_ndi._tcp`) for as long as OBS is running
   with NDI Output enabled.

> Automation is tied to the **NDI Output state** (enabled/disabled). The reliable way to start the
> live feed for the congregation is to enable NDI Output at the start of the service and disable it
> at the end. Tying detection to "Start Streaming" / "Start Recording" is not directly supported
> because those actions do not affect NDI advertisement.

## PlainSight configuration

1. In the dashboard, open **NDI Sources**.
   - Click **Add Source** and type the exact name you set in OBS NDI Output Settings
     (e.g. `CHURCH-PC (Sanctuary-Livestream)`). This is the most reliable approach.
   - Alternatively, auto-discovery will find OBS sources that advertise via mDNS within ~30 seconds —
     but this depends on your OS/network configuration and may not work in all environments.
2. Open **Devices**, click the **Live Mode** button on each TV that should switch to the feed.
3. In the dialog:
   - Set the **Assigned NDI source** to the OBS output.
   - Tick **Auto-switch when assigned NDI source appears on the network**.
   - Leave the **Manual override** dropdown on **Auto** for normal operation.

## Manual override

Use the **Manual override** dropdown on a per-device basis to force the behavior:

| Setting     | Behavior                                                            |
|-------------|---------------------------------------------------------------------|
| Auto        | Follow the auto-switch logic (live when assigned source is online). |
| Force ON    | Always show the live NDI feed, ignoring source presence.            |
| Force OFF   | Always show signage, even if the assigned source is broadcasting.   |

## Workflow

1. Start of service: operator enables **Tools → NDI Output → Main Output** in OBS.
2. PlainSight Server detects the source via mDNS.
3. Devices configured for auto-switch receive `LiveMode=true` on their next heartbeat (within 30s).
4. The Player kills the kiosk video, launches the configured NDI viewer (e.g. `dicaffeine`), and the
   TV displays the live feed.
5. End of service: operator disables NDI Output in OBS.
6. The source disappears from mDNS; within `Ndi:StalenessSeconds` (default 60s) PlainSight reports
   `LiveMode=false` and the TV reverts to the cached signage playlist.

## Fail-safe

If the player loses contact with the PlainSight Server for 3 consecutive heartbeats (~90 seconds),
the player automatically kills the NDI viewer process and reverts to the locally cached signage
playlist. This guarantees the screen never goes black if the server is offline mid-service.

## Player configuration

The Raspberry Pi player launches an external NDI viewer when commanded by the server. Configure the
viewer via environment variables on the player:

| Variable          | Default        | Description                                                       |
|-------------------|----------------|-------------------------------------------------------------------|
| `NdiViewerPath`   | `dicaffeine`   | Path to the NDI viewer executable (e.g. yuri2 / dicaffeine on Pi).|
| `NdiViewerArgs`   | `--fullscreen --source "{0}"` | Arguments format string. `{0}` is replaced with the NDI source name. |

The viewer must be installed on the Pi separately. For Raspberry Pi 5, hardware-accelerated NDI
decoding is available via `dicaffeine`; install the binary on each Pi and set `NdiViewerPath`
accordingly.

## Server configuration

### OBS WebSocket (recommended)

OBS 28+ has a built-in WebSocket server. Enable it under **Tools → WebSocket Server Settings** in OBS.

| Setting                   | Default | Description                                                                |
|---------------------------|---------|----------------------------------------------------------------------------|
| `OBS__WebSocketUrl`       | *(none)* | WebSocket URL, e.g. `ws://192.168.1.50:4455`. Leave empty to disable.    |
| `OBS__WebSocketPassword`  | *(none)* | OBS WebSocket password (if authentication is enabled).                    |
| `OBS__NdiOutputName`      | `NDI Output` | Name of the OBS output to monitor (default matches OBS-NDI plugin). |
| `OBS__NdiSourceName`      | *(none)* | The NDI source name to mark live when OBS output is active, e.g. `CHURCH-PC (Sanctuary-Livestream)`. Must match a source in the NDI Sources list. |

> Use double-underscores (`__`) in environment variables to represent nested config keys (e.g. `OBS__WebSocketUrl`).
> In `appsettings.json` use the nested form: `{ "OBS": { "WebSocketUrl": "ws://..." } }`.

When OBS WebSocket is configured, PlainSight connects to OBS at startup and reconnects automatically.
The NDI Sources page shows the connection status and whether the NDI Output is currently active.

### mDNS auto-discovery (fallback)

The server also scans for NDI sources advertised via mDNS (`_ndi._tcp.local.`). This may work
depending on OBS-NDI plugin version and network configuration.

| Setting                       | Default | Description                                                              |
|-------------------------------|---------|--------------------------------------------------------------------------|
| `Ndi:ScanIntervalSeconds`     | 15      | How often the server re-scans mDNS for NDI sources.                      |
| `Ndi:ScanTimeoutSeconds`      | 4       | How long each scan listens for responses.                                |
| `Ndi:StalenessSeconds`        | 60      | Source is considered offline if not seen within this window.             |
