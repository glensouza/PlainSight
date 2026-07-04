# Live Video (NDI)

How to switch signage screens to a live NDI feed — e.g. a church service broadcast — and back to cached signage when the feed ends.

For the hardware/software setup of the NDI source itself (OBS, networking, NDI plugins), see [NDI & OBS Setup](../NDI-OBS-Setup.md). This guide covers the operator side in the PlainSight admin UI.

## Quick Path

1. Have your streaming PC running OBS with the OBS-NDI plugin and NDI Output enabled.
2. In PlainSight, navigate to **NDI Sources** in the sidebar.
3. Click **Add Source** and enter the exact source name (e.g. `CHURCH-PC (Sanctuary-Livestream)`).
4. Go to **Devices**, click **Live Mode** on each TV.
5. Set the **Assigned NDI source**, tick **Auto-switch**, leave Manual on **Auto**.
6. When the operator enables NDI Output in OBS, the feed goes live on all assigned TVs within 30 seconds.

## NDI Source Discovery

The server discovers NDI sources in two ways:

### Automatic (mDNS)
`NdiDiscoveryService` scans the network every `Ndi:ScanIntervalSeconds` (default 15 s) for `_ndi._tcp` services. Discovered sources appear automatically in the NDI Sources page.

### Manual
Click **Add Source** and type the exact name. Use this when mDNS discovery is unreliable on your network.

## Device Assignment

On the **Devices** page, click the **Live Mode** button for each TV that should receive the live feed:

| Setting | Description |
|---|---|
| **Assigned NDI source** | Which source this device should switch to when live. |
| **Auto-switch** | When checked, the device automatically enters live mode when the assigned source is online. |
| **Manual override** | **Auto**: Follow auto-switch logic. **Force ON**: Always show live feed. **Force OFF**: Never show live feed. |

## Auto-Switch Logic

On each heartbeat, the server checks:

1. If the assigned NDI source was seen within `Ndi:StalenessSeconds` (default 60 s), it's "fresh" → `liveMode = true`.
2. If OBS WebSocket is connected and the configured output is active, the source is forced fresh.
3. Manual override (Force ON/OFF) takes precedence over all auto-switch logic.

The heartbeat response includes `liveMode` and `ndiSourceName`. When `liveMode` is `true`, the player kills the Chromium kiosk and launches the configured NDI viewer (`dicaffeine` by default).

## Ending the Live Feed

- **Operator action**: Disable NDI Output in OBS. The source becomes stale within 60 seconds.
- **Manual override**: Set any device to **Force OFF**.
- **End Live button**: On the NDI Sources page, click **End Live** to clear live mode on all devices (respects the target group filter).

## Fail-Safe

If the player loses contact with the server for 3 consecutive heartbeats (~90 s), it automatically kills the NDI viewer and reverts to the locally cached signage playlist. The screen never goes black.

## OBS WebSocket Integration

Configure OBS WebSocket for automatic live detection without relying on mDNS timing:

1. Enable the WebSocket server in OBS (**Tools → WebSocket Server Settings**).
2. Set config keys:
   - `OBS:WebSocketUrl` = `ws://192.168.1.50:4455`
   - `OBS:WebSocketPassword` = (if auth is enabled)
   - `OBS:NdiSourceName` = e.g. `CHURCH-PC (Sanctuary-Livestream)`
3. The NDI Sources page shows connection status and whether the NDI Output is active.
4. Use the **Sync with Streaming** / **Sync with Recording** toggles to control whether OBS streaming/recording state triggers live mode.

See [NDI & OBS Setup](../NDI-OBS-Setup.md) for detailed configuration of the OBS side.
