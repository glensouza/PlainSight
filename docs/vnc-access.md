# VNC Access to Raspberry Pi Players

The players run Chromium in kiosk mode under **labwc**, which is a **Wayland** compositor
(user `pi`, autologin → `exec labwc`, `WAYLAND_DISPLAY=wayland-0`). Because the screen is
composited by Wayland, the classic X11 tools (`x11vnc`, RealVNC's legacy server) cannot
capture it. The right tool is **`wayvnc`**, which uses the same `wlr-screencopy` protocol the
player's `grim` screenshots already rely on, so it mirrors the live signage output.

Player hostnames (mDNS): `plainsight-sanctuary.local` (`10.0.10.200`),
`plainsight-office.local` (`10.0.10.232`).

## The short version (Raspberry Pi OS Trixie)

On Raspberry Pi OS **Trixie** (Debian 13, the current player image), wayvnc is the OS's
**built-in VNC server** and is managed for you. Installing the package is the whole setup:

```bash
ssh pi@plainsight-sanctuary.local
sudo apt install -y wayvnc
```

The `wayvnc` package installs and **auto-enables** a systemd service (`wayvnc.service`,
preset `enabled`) that:

- starts on every boot (`Restart=always`), running as a dedicated `vnc` user;
- captures the **live HDMI output** via GPU (`wayvnc --gpu`), so you see the actual signage;
- listens on **all interfaces, port 5900** (`address=::` in `/etc/wayvnc/config`);
- is **authenticated + encrypted out of the box** — `enable_auth=true` + `enable_pam=true`,
  with TLS/RSA keys generated automatically on first boot by `wayvnc-generate-keys.service`.

Verify it came up enabled and running:

```bash
systemctl is-enabled wayvnc.service      # expect: enabled
systemctl status wayvnc.service --no-pager
ss -tlnp | grep 5900                      # expect *:5900 owned by wayvnc
```

If for some reason it is not enabled (older image, or VNC was toggled off), enable it:

```bash
sudo systemctl enable --now wayvnc.service
```

> **Do not** add a `wayvnc` line to `~/.config/labwc/autostart`. The systemd service already
> owns port 5900; a second instance from autostart would just fail to bind. Startup is handled.

## Connecting

Because the built-in service is already authenticated over an encrypted channel, you can
connect **directly over the LAN — no SSH tunnel required**.

Point a VNC viewer at:

```
plainsight-sanctuary.local:5900      (or 10.0.10.200:5900)
plainsight-office.local:5900         (or 10.0.10.232:5900)
```

Log in with the Pi's normal login: username **`pi`** and its password (PAM authenticates
against the system account). You will see exactly what is on the HDMI output — the live
signage.

### Fitting the 1920×1080 screen to your window

The signage runs at 1920×1080, so on a smaller monitor you want the viewer to scale the
remote screen down to fit. Support for this **varies by viewer and build**:

- **RealVNC Viewer** — reliable fit-to-window. Recommended desktop client.
- **TigerVNC Viewer** — *if* your build ships the Options → **Screen** tab, set **Scaling
  factor** to **Auto**. Some Windows TigerVNC packages omit that tab entirely, in which case
  there is no scaling option — use RealVNC Viewer instead.
- **In-app Live View** (Devices page) — the built-in noVNC viewer scales to the browser
  window automatically (`scaleViewport`), so there is nothing to configure. See below.

## In-app Live View (Devices page)

The admin site can render the live signage in the browser without any external viewer. Each
online device tile has a **Live View** action (Admin only) that opens a full-window,
view-only noVNC canvas which auto-scales to fit.

How it works: the server exposes an Admin-authorized WebSocket proxy at
`/api/device/{deviceId}/vnc` that pipes bytes to the player's wayvnc on `:5900` (the target
host comes from the device's `CallbackUrl`). The proxy is a **dumb byte pipe** — wayvnc's
RSA-AES/TLS handshake stays end-to-end with noVNC, so no player credentials pass through the
server. You are prompted for the Pi's username (`pi`) and password in the viewer; nothing is
stored server-side. noVNC is bundled offline under `wwwroot/lib/novnc` (v1.7.0).

> Note: VNC streams full frames, so a screen playing motion video is bandwidth-heavy per open
> viewer. Live View is intended for spot-checking a device, not leaving many tiles streaming.

## Configuration reference

The service reads `/etc/wayvnc/config`. On a stock Trixie player it looks like:

```ini
use_relative_paths=true
address=::
enable_auth=true
enable_pam=true
private_key_file=tls_key.pem
certificate_file=tls_cert.pem
rsa_private_key_file=rsa_key.pem
```

Relevant systemd units:

| Unit | Role |
|---|---|
| `wayvnc.service` | The VNC server; runs `/usr/sbin/wayvnc-run.sh` as user `vnc` |
| `wayvnc-generate-keys.service` | Generates the TLS cert + RSA key on first start (required dep) |
| `wayvnc-control.service` | Control socket (`wayvncctl`) |

After editing the config, restart the service: `sudo systemctl restart wayvnc.service`.

### Optional: restrict to localhost + SSH tunnel

The default binds to the LAN. It is authenticated, but if you would rather the port not be
reachable on the network at all, set `address=localhost` in `/etc/wayvnc/config`,
`sudo systemctl restart wayvnc.service`, and reach it through an SSH tunnel:

```powershell
ssh -L 5900:localhost:5900 pi@plainsight-sanctuary.local
```

Then point the viewer at `localhost:5900`.

## Troubleshooting

### `apt install` fails with "No space left on device" but `df -h /` shows free space

apt writes its temporary index files to **`/tmp`**, which on the player is a RAM-backed
`tmpfs` (2 GB), separate from the root filesystem. If a process filled `/tmp` (or left a
large deleted-but-open file there), apt gets `ENOSPC` even though `/` has gigabytes free.
Check the tmpfs specifically:

```bash
df -h /tmp        # if Use% is 100%, this is the cause, not the SD card
```

Because `/tmp` is RAM-backed, a **reboot clears it completely** and restarts whatever was
holding the space:

```bash
sudo reboot
```

Then retry `sudo apt install -y wayvnc`. (The SD card itself is fine as long as
`dmesg | grep -iE 'mmc|read-only|I/O error'` is clean and `/` is not near full.)

### Nothing is listening on 5900

```bash
systemctl status wayvnc.service --no-pager
journalctl -u wayvnc.service -n 30 --no-pager
```

A common cause is `wayvnc-generate-keys.service` not having produced the TLS/RSA keys yet;
it runs as a dependency, so `sudo systemctl restart wayvnc.service` usually resolves it.

See the [wayvnc README](https://github.com/any1/wayvnc) for the full list of config keys.
</content>
