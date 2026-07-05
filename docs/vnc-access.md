# VNC Access to Raspberry Pi Players

The players run Chromium in kiosk mode under **labwc**, which is a **Wayland** compositor
(user `pi`, autologin → `exec labwc`, `WAYLAND_DISPLAY=wayland-0`). Because the screen is
composited by Wayland, the classic X11 tools (`x11vnc`, RealVNC's legacy server) cannot
capture it. The right tool is **`wayvnc`**, which uses the same `wlr-screencopy` protocol the
player's `grim` screenshots already rely on, so it mirrors the live signage output.

Player hostnames (mDNS): `plainsight-sanctuary.local`, `plainsight-office.local`.

## One-time install (per Pi)

```bash
ssh pi@plainsight-sanctuary.local
sudo apt update && sudo apt install -y wayvnc
```

## Recommended: SSH tunnel (secure, no VNC password needed)

`wayvnc` has no authentication out of the box, so bind it to localhost and reach it through
SSH, which already provides authentication and encryption.

On the Pi, start `wayvnc` inside the running labwc session:

```bash
export XDG_RUNTIME_DIR=/run/user/1000
export WAYLAND_DISPLAY=wayland-0        # if this errors, check: ls $XDG_RUNTIME_DIR/wayland-*
wayvnc localhost 5900
```

On Windows, open a tunnel and connect a viewer:

```powershell
ssh -L 5900:localhost:5900 pi@plainsight-sanctuary.local
```

Then in **RealVNC Viewer** or **TigerVNC Viewer**, connect to `localhost:5900`. You will see
exactly what is on the HDMI output.

## Optional: always-on

To keep it running after every reboot, add this line to `~/.config/labwc/autostart` on the Pi
(keep it bound to localhost and still reach it via the SSH tunnel above):

```bash
wayvnc localhost 5900 &
```

## Exposing on the LAN directly (authenticated)

If you would rather connect without an SSH tunnel (`wayvnc 0.0.0.0 5900`), do **not** do so
without enabling authentication first — these are unattended devices on a shared network.
`wayvnc` reads its settings from `~/.config/wayvnc/config`. Two mechanisms are available.

### Option A — TLS / VeNCrypt (works on older `wayvnc`)

Username + password protected over a TLS-encrypted channel.

1. Generate a self-signed key/cert on the Pi:

   ```bash
   mkdir -p ~/.config/wayvnc
   openssl req -x509 -nodes -newkey rsa:2048 \
     -keyout ~/.config/wayvnc/tls_key.pem \
     -out ~/.config/wayvnc/tls_cert.pem \
     -days 3650 -subj "/CN=plainsight-player"
   chmod 600 ~/.config/wayvnc/tls_key.pem
   ```

2. Write `~/.config/wayvnc/config`:

   ```ini
   address=0.0.0.0
   port=5900
   enable_auth=true
   username=plainsight
   password=CHANGE_ME
   private_key_file=/home/pi/.config/wayvnc/tls_key.pem
   certificate_file=/home/pi/.config/wayvnc/tls_cert.pem
   ```

3. Start it (reads the config automatically):

   ```bash
   export XDG_RUNTIME_DIR=/run/user/1000
   export WAYLAND_DISPLAY=wayland-0
   wayvnc
   ```

> **Client note:** TLS/VeNCrypt is supported by **TigerVNC Viewer**, but **not** by RealVNC
> Viewer. Use TigerVNC for this option, connecting to `plainsight-sanctuary.local:5900` with
> the username/password above.

### Option B — RSA-AES (cert-free, newer `wayvnc` ≥ 0.7)

Encrypts the session and authenticates without generating TLS certificates.

1. Generate an RSA key:

   ```bash
   mkdir -p ~/.config/wayvnc
   openssl genpkey -algorithm RSA -out ~/.config/wayvnc/rsa_key.pem
   chmod 600 ~/.config/wayvnc/rsa_key.pem
   ```

2. `~/.config/wayvnc/config`:

   ```ini
   address=0.0.0.0
   port=5900
   enable_auth=true
   username=plainsight
   password=CHANGE_ME
   rsa_private_key_file=/home/pi/.config/wayvnc/rsa_key.pem
   ```

3. Start it the same way as Option A. Check your packaged version with `wayvnc --version`; if
   the config is rejected, the build predates RSA-AES — use Option A instead.

### Always-on with auth

Once a config is in place, replace the localhost autostart line in `~/.config/labwc/autostart`
with the config-driven form (it binds to `address` from the config):

```bash
wayvnc &
```

See the [wayvnc README](https://github.com/any1/wayvnc) for the full list of config keys.
