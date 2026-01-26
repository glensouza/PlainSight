# PlainSight Photino Player

## Overview

The Photino Player is a lightweight, native desktop application for displaying digital signage content on Raspberry Pi devices. It uses **Photino.NET**, a cross-platform framework that provides native window management with HTML5 content rendering.

## Features

- **Native Performance**: Uses OS-native WebKit for video playback
- **Fullscreen Kiosk Mode**: Chromeless, fullscreen window for dedicated signage displays
- **HTML5 Video Player**: Smooth video playback with auto-loop and playlist support
- **Automatic Content Discovery**: Scans `/mnt/signage/content` for video files or reads `playlist.json`
- **Heartbeat Integration**: Reports status to server every 30 seconds
- **Self-Updating**: Downloads and installs updates automatically
- **Screenshot Capture**: On-demand screenshots via grim (Wayland)
- **Debug Overlay**: Press Ctrl+D to toggle status information

## Architecture

```
┌─────────────────────────────────────────┐
│     PlainSight Photino Player           │
│  ┌───────────────────────────────────┐  │
│  │      Photino Window (Native)      │  │
│  │  ┌─────────────────────────────┐  │  │
│  │  │   WebKit Content View       │  │  │
│  │  │   ┌───────────────────────┐ │  │  │
│  │  │   │  HTML5 Video Player   │ │  │  │
│  │  │   │  - Auto-loop          │ │  │  │
│  │  │   │  - Playlist support   │ │  │  │
│  │  │   │  - Error handling     │ │  │  │
│  │  │   └───────────────────────┘ │  │  │
│  │  └─────────────────────────────┘  │  │
│  └───────────────────────────────────┘  │
│                                          │
│  Services:                               │
│  - HeartbeatService                      │
│  - UpdateService                         │
│  - ScreenCaptureService                  │
│  - PlaylistService                       │
└─────────────────────────────────────────┘
         │                  │
         │ Heartbeat API    │ SMB Share
         ▼                  ▼
    PlainSight Server   /mnt/signage/content
```

## Supported Video Formats

- MP4 (H.264/AAC)
- WebM (VP8/VP9/Vorbis/Opus)
- MKV
- AVI
- MOV

## Content Management

### Playlist File (Recommended)

Create `/mnt/signage/content/playlist.json`:

```json
{
  "Items": [
    "welcome.mp4",
    "announcements.mp4",
    "sermon-intro.mp4"
  ]
}
```

### Auto-Discovery

If no `playlist.json` exists, the player automatically scans the content directory for all video files and plays them alphabetically.

## Configuration

### Environment Variables

- `ServerUrl`: PlainSight server URL (default: `https://localhost:7149/`)
- `ContentPath`: Path to video content (default: `/mnt/signage/content`)
- `WAYLAND_DISPLAY`: Wayland display socket (default: `wayland-1`)
- `DISPLAY`: X11 display (fallback, default: `:0`)

### Command Line Arguments

```bash
./Signage.Player.Photino --ServerUrl=http://192.168.1.100:8080 --ContentPath=/mnt/signage/content
```

## Installation

### Option 1: Automated Script

```bash
curl -sSL https://raw.githubusercontent.com/glensouza/PlainSight/main/deployment/raspberry-pi/install-photino.sh | bash
```

### Option 2: Manual Installation

1. **Install Dependencies**:
```bash
sudo apt install -y \
  labwc \
  wayland-protocols \
  cifs-utils \
  grim \
  swayidle \
  wlopm \
  libgtk-3-0 \
  libwebkit2gtk-4.1-0
```

2. **Download Binary**:
```bash
sudo mkdir -p /opt/signage
curl -L "http://SERVER_IP:8080/api/updates/latest/photino-binary" \
  -o /opt/signage/Signage.Player.Photino
chmod +x /opt/signage/Signage.Player.Photino
```

3. **Install Systemd Service**:
```bash
sudo cp deployment/raspberry-pi/systemd/signage-photino.service \
  /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable signage-photino.service
```

4. **Configure labwc**:
```bash
mkdir -p ~/.config/labwc
cp deployment/raspberry-pi/config/labwc-rc-photino.xml \
  ~/.config/labwc/rc.xml
cp deployment/raspberry-pi/config/labwc-autostart-photino \
  ~/.config/labwc/autostart
chmod +x ~/.config/labwc/autostart
```

## Development

### Building from Source

```bash
# Build for development (x64)
cd src/Signage.Player.Photino
dotnet build

# Build for Raspberry Pi (ARM64)
dotnet publish -r linux-arm64 --self-contained -p:PublishSingleFile=true
```

### Testing Locally

```bash
# Set environment variables
export ServerUrl=http://localhost:8080
export ContentPath=/path/to/test/videos

# Run the player
dotnet run --project src/Signage.Player.Photino
```

### Debug Mode

Press **Ctrl+D** while the player is running to toggle the debug status overlay, which shows:
- Current timestamp
- Currently playing file
- Playlist position
- Any error messages

## Troubleshooting

### Player Won't Start

Check systemd logs:
```bash
sudo journalctl -u signage-photino.service -f
```

### Video Won't Play

1. Verify content path:
```bash
ls -la /mnt/signage/content
```

2. Check SMB mount:
```bash
mount | grep signage
```

3. Test video file manually:
```bash
ffprobe /mnt/signage/content/your-video.mp4
```

### Black Screen

1. Verify Wayland is running:
```bash
echo $WAYLAND_DISPLAY
```

2. Check labwc configuration:
```bash
cat ~/.config/labwc/rc.xml
```

3. Restart the player:
```bash
sudo systemctl restart signage-photino.service
```

## Performance

### Recommended Settings

- **Resolution**: 1080p (1920x1080) for best compatibility
- **Codec**: H.264 (hardware accelerated on Raspberry Pi 5)
- **Bitrate**: 5-10 Mbps for 1080p content
- **Frame Rate**: 30fps or 60fps

### Hardware Requirements

- Raspberry Pi 5 (4GB or 8GB)
- Active cooling (mandatory)
- Gigabit Ethernet
- Industrial-grade MicroSD card (32GB+)

## Comparison: Photino vs Console Player

| Feature | Console Player | Photino Player |
|---------|---------------|----------------|
| UI Framework | None (console only) | Native window with HTML5 |
| Video Playback | External (mpv, vlc) | Built-in (WebKit) |
| Window Management | Manual | Automatic fullscreen |
| Content Loading | Playlist logic required | Auto-discovery |
| Debug Interface | Log files only | Overlay (Ctrl+D) |
| Memory Usage | ~50MB | ~150MB |
| Dependencies | mpv/vlc | libwebkit2gtk |

## Technical Details

### Technology Stack

- **.NET 8**: Target framework (Photino.NET 4.0.16 compatibility)
- **Photino.NET 4.0.16**: Native window manager
- **WebKit2GTK**: HTML5 rendering engine
- **GLib/GTK3**: UI framework
- **Wayland**: Display protocol (via labwc)

### Custom Scheme Handler

The player registers a custom `app://` scheme to access local video files:

```javascript
// In HTML:
<video src="app:///mnt/signage/content/video.mp4"></video>

// Handled by C#:
RegisterCustomSchemeHandler("app", (sender, scheme, url, out contentType) => {
    contentType = "video/mp4";
    return File.OpenRead(url.Replace("app://", ""));
});
```

## License

Copyright (c) 2026. All rights reserved.

## Support

For issues and questions, please open an issue on the [GitHub repository](https://github.com/glensouza/PlainSight).
