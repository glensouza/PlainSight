# VNC Access to Raspberry Pi Players

The players run Chromium in kiosk mode under **labwc**, a **Wayland** compositor (user `pi`,
autologin → `exec labwc`, `WAYLAND_DISPLAY=wayland-0`). Because the screen is composited by
Wayland, the classic X11 tools (`x11vnc`, RealVNC's legacy server) cannot capture it. The
right tool is **`wayvnc`**, which uses the same `wlr-screencopy` protocol the player's `grim`
screenshots rely on, so it mirrors the live signage output.

Player hostnames (mDNS): `plainsight-sanctuary.local` (`10.0.10.200`),
`plainsight-office.local` (`10.0.10.232`).

## Primary way in: Live View on the Devices page

The admin site streams a player's live screen into the browser — no VNC client to install.
On the **Devices** page, each online device has a **Live View** button (Admin only) that opens
a full-window, **view-only** viewer which **auto-scales to fit** your browser window and
connects on its own (no password prompt).

Under the hood:

- The server exposes an Admin-authorized WebSocket endpoint, `/api/device/{deviceId}/vnc`,
  that proxies raw bytes to the player's `wayvnc` on port `5900`. The target host comes from
  the device's `CallbackUrl` (reported in its heartbeat).
- The browser client is **noVNC** (bundled offline under
  `src/PlainSight.Server/wwwroot/lib/novnc`, v1.7.0), rendering to a `<canvas>`.
- The endpoint accepts both `GET` and `CONNECT` methods. Over HTTPS the server speaks HTTP/2,
  and browsers open WebSockets as an HTTP/2 Extended `CONNECT` rather than a `GET`; accepting
  both is what makes Live View work over HTTP/2.

Security of this path: the browser↔server hop is normal `wss` protected by the admin login
cookie (Admin role required). The server↔player hop is plain TCP on the LAN.

> VNC streams full frames, so a screen playing motion video is bandwidth-heavy per open
> viewer. Live View is for spot-checking a device, not leaving many tiles streaming at once.

## Player-side setup (per Pi)

`wayvnc` is installed as part of the [Raspberry Pi setup](raspberry-pi-setup.md) (it is in the
dependency list). Installing the package auto-enables a systemd service (`wayvnc.service`) that
runs on boot as a dedicated `vnc` user and captures the live HDMI output.

The player must be configured to accept the connection from noVNC. Out of the box wayvnc
negotiates an encrypted RSA-AES / PAM login that the browser client cannot complete, so the
players are configured to accept **unauthenticated** connections. Write `/etc/wayvnc/config`:

```bash
sudo tee /etc/wayvnc/config >/dev/null <<'EOF'
use_relative_paths=true
address=::
enable_auth=false
EOF
sudo systemctl restart wayvnc.service
```

Verify:

```bash
systemctl is-enabled wayvnc.service     # expect: enabled
systemctl is-active wayvnc.service      # expect: active
ss -tlnp | grep 5900                     # expect *:5900
```

> **Do not** add a `wayvnc` line to `~/.config/labwc/autostart`. The systemd service already
> owns port 5900; a second instance would fail to bind. Startup is handled.

### Why unauthenticated, and the trade-off

`enable_auth=false` means port 5900 accepts VNC from any host on the LAN with no password.
This is deliberate:

- The player's screen is **public signage content** — anyone can already see it on the wall,
  so unauthenticated *viewing* leaks nothing.
- The browser Live View is the intended entry point, and it *is* authenticated (admin login).

The real residual risk is that someone on the LAN could open a VNC client and send **input**
to the Pi. On a trusted church/office network that is acceptable; **keep these players off
untrusted networks**. If you need it locked down, see [Hardening options](#hardening-options).

## Alternative: a desktop VNC viewer

Because auth is disabled, any VNC viewer connects with **no password**. Point it at:

```
plainsight-sanctuary.local:5900      (or 10.0.10.200:5900)
plainsight-office.local:5900         (or 10.0.10.232:5900)
```

### Fitting the 1920×1080 screen to your window

The signage runs at 1920×1080, so on a smaller monitor you want the viewer to scale to fit.
Support varies by client:

- **In-app Live View** — scales automatically (`scaleViewport`); nothing to configure.
  Preferred, and free.
- **TigerVNC Viewer** — *if* your build has the Options → **Screen** tab, set **Scaling
  factor** to **Auto**. Some Windows TigerVNC packages omit that tab, in which case there is no
  scaling option.
- **RealVNC Viewer** — scales reliably, but recent versions require a paid subscription to
  connect, so it is no longer a free option.

Given the desktop-client caveats, the in-app Live View is the recommended way to view a player.

## Hardening options

If unauthenticated LAN access is unacceptable in your environment:

- **Bind wayvnc to localhost** (`address=localhost` in `/etc/wayvnc/config`) and reach it over
  an SSH tunnel for direct desktop-viewer use:

  ```powershell
  ssh -L 5900:localhost:5900 pi@plainsight-sanctuary.local
  ```

  Note this **disables the in-app Live View**, because the server reaches the player over the
  LAN, not localhost.

- **Authenticate at the proxy instead of the player** — a future option is to have the server
  perform the wayvnc RSA-AES handshake upstream and present an unauthenticated stream only to
  the already-authenticated browser, so the players could keep `enable_auth=true`. This is not
  implemented; it requires an RSA-AES RFB client on the server.

## Troubleshooting

### `apt install` fails with "No space left on device" but `df -h /` shows free space

apt writes temporary index files to **`/tmp`**, a RAM-backed `tmpfs` (2 GB) separate from the
root filesystem. If a process filled `/tmp` (or left a large deleted-but-open file there), apt
gets `ENOSPC` even though `/` has gigabytes free:

```bash
df -h /tmp        # if Use% is 100%, this is the cause, not the SD card
```

`/tmp` is RAM-backed, so a **reboot clears it** and restarts whatever was holding the space:

```bash
sudo reboot
```

Then retry the install. (The SD card is fine as long as
`dmesg | grep -iE 'mmc|read-only|I/O error'` is clean and `/` is not near full.)

### Live View shows "Disconnected"

```bash
# On the player:
systemctl is-active wayvnc.service       # must be active
ss -tlnp | grep 5900                      # must be listening on *:5900
grep enable_auth /etc/wayvnc/config       # must be enable_auth=false
```

Also confirm the device is **online** in the Devices page (Live View only appears for online
devices) and that the server host can reach the player's IP on port 5900.

See the [wayvnc README](https://github.com/any1/wayvnc) for the full list of config keys.
