# Raspberry Pi Setup Guide

This guide walks you through setting up a Raspberry Pi 5 as a PlainSight digital signage player.

## Hardware Requirements

### Required Components

- **Raspberry Pi 5** (4GB or 8GB model)
- **Official Raspberry Pi Active Cooler** (mandatory for 4K video)
- **Industrial-grade MicroSD Card** (32GB+, SLC or pSLC recommended)
- **Quality Power Supply** (Official Raspberry Pi 27W USB-C recommended)
- **Ethernet Cable** (Gigabit network required)
- **HDMI 2.1 Cable** (for 4K@60Hz support)
- **Display** (HDMI 2.1 compatible)

### Optional Components

- **Case** (with adequate ventilation)
- **NVMe HAT** (for OS storage, though not required since content is streamed)

## Software Requirements

- **Raspberry Pi OS Lite (64-bit)** - Debian Bookworm based
- **labwc** - Wayland compositor
- **.NET 10 Runtime** - Included in the Player binary (self-contained)

## Installation Methods

### Method 1: Automated Installation (Recommended)

The automated script handles all configuration steps.

#### Step 1: Flash Raspberry Pi OS

1. Download [Raspberry Pi Imager](https://www.raspberrypi.com/software/)
2. Flash **Raspberry Pi OS Lite (64-bit)** to your MicroSD card
3. In Imager settings:
   - Set hostname: `plainsight-player-01` (or similar)
   - Enable SSH
   - Set username: `pi`
   - Set password
   - Configure WiFi (temporary, will switch to Ethernet)

#### Step 2: Boot and Connect

1. Insert MicroSD card into Raspberry Pi
2. Connect Ethernet cable
3. Connect HDMI to display
4. Power on the device
5. SSH into the device: `ssh pi@plainsight-player-01.local`

#### Step 2b: Set Up Passwordless SSH (Windows)

After first SSH login, copy your public key to the Pi so future connections
(and remote management) don't require a password. On Windows, `ssh-copy-id`
is not available — use this PowerShell equivalent instead:

```powershell
# Generate a key pair if you don't have one yet (run once per machine)
ssh-keygen -t ed25519 -f "$env:USERPROFILE\.ssh\id_ed25519" -C "plainsight-admin"

# Copy the public key to the Pi (you will be prompted for the Pi password once)
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh pi@plainsight-player-01.local "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys && chmod 700 ~/.ssh"
```

After this, `ssh pi@plainsight-player-01.local` connects without a password.
Repeat the `type ... | ssh ...` line for each additional Pi.

#### Step 3: Run Installation Script

```bash
# Download the installer
curl -sSL https://raw.githubusercontent.com/glensouza/PlainSight/main/deployment/raspberry-pi/install.sh -o install.sh

# Make it executable
chmod +x install.sh

# Run the installer
./install.sh
```

The script will:
- Update system packages
- Install required dependencies
- Download the Player binary
- Configure systemd services
- Set up labwc window manager
- Enable automatic startup

#### Step 4: Reboot

The script will prompt you to reboot. After reboot, the Player starts automatically.

### Method 2: Manual Installation

If you prefer to understand each step or need to customize the installation:

#### Step 1: Update System

```bash
sudo apt update
sudo apt upgrade -y
```

#### Step 2: Install Dependencies

```bash
sudo apt install -y \
  chromium \
  labwc \
  wayland-protocols \
  cifs-utils \
  grim \
  swayidle \
  swaybg \
  wlopm \
  wayvnc \
  curl
```

`wayvnc` provides the VNC server used by the admin **Live View** feature (see
[Remote viewing (VNC)](#remote-viewing-vnc) below and [docs/vnc-access.md](vnc-access.md)).

#### Step 3: Create Directories

```bash
sudo mkdir -p /opt/plainsight
sudo mkdir -p /mnt/signage
sudo chown pi:pi /opt/plainsight
```

#### Step 4: Download Player Binary

Replace `SERVER_IP` with your server's IP address:

```bash
curl -L "http://SERVER_IP:8080/api/updates/latest/binary" \
  -o /opt/plainsight/PlainSight.Player
chmod +x /opt/plainsight/PlainSight.Player
```

#### Step 5: Configure SMB Mount

Create `/etc/systemd/system/mnt-signage.mount`:

```ini
[Unit]
Description=Mount Remote Signage Assets
After=network-online.target

[Mount]
What=//SERVER_IP/signage
Where=/mnt/signage
Type=cifs
Options=username=pi,password=secure,rw,vers=3.0

[Install]
WantedBy=multi-user.target
```

Create `/etc/systemd/system/mnt-signage.automount`:

```ini
[Unit]
Description=Automount Signage Share

[Automount]
Where=/mnt/signage
TimeoutIdleSec=0

[Install]
WantedBy=multi-user.target
```

#### Step 6: Configure Player Service

Create `/etc/systemd/system/plainsight.service`:

```ini
[Unit]
Description=PlainSight Digital Signage Player
After=network-online.target mnt-signage.mount
Wants=mnt-signage.mount

[Service]
Type=simple
User=pi
WorkingDirectory=/opt/plainsight
ExecStart=/opt/plainsight/PlainSight.Player
Restart=always
RestartSec=3
Environment=DISPLAY=:0
Environment=WAYLAND_DISPLAY=wayland-0
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1
Environment=ServerUrl=http://SERVER_IP:8080
Environment=PLAINSIGHT_APIKEY_PATH=/var/cache/plainsight/apikey

[Install]
WantedBy=graphical.target
```

#### Step 7: Configure labwc

Create `~/.config/labwc/rc.xml`:

```xml
<labwc_config>
  <windowRules>
    <windowRule identifier="PlainSight.Player">
      <action name="ToggleFullscreen" />
      <action name="KeepAbove" />
    </windowRule>
  </windowRules>
</labwc_config>
```

Create `~/.config/labwc/autostart`:

```bash
#!/bin/bash
# Disable screen sleep/power saving
swayidle -w timeout 31536000 'wlopm --off \*' resume 'wlopm --on \*' &

# Start PlainSight Player
/opt/plainsight/PlainSight.Player &
```

Make it executable:

```bash
chmod +x ~/.config/labwc/autostart
```

#### Step 8: Enable Services

```bash
sudo systemctl daemon-reload
sudo systemctl enable mnt-signage.automount
sudo systemctl enable plainsight.service
```

#### Step 9: Reboot

```bash
sudo reboot
```

## Post-Installation

### Verify Installation

After reboot, check the service status:

```bash
# Check if player is running
sudo systemctl status plainsight.service

# Check if SMB share is mounted
mount | grep signage

# View player logs
sudo journalctl -u plainsight.service -f
```

### Configure in Admin Panel

1. Open the admin web interface at `http://SERVER_IP:8080`
2. Navigate to Devices
3. You should see your new device listed
4. Set the device name and group

## Remote viewing (VNC)

The admin site's **Live View** button (on each online device in the Devices page) streams the
player's live screen into the browser. It talks to `wayvnc` on the Pi, so each player needs
`wayvnc` installed (it is in the dependency list above) and configured to accept the
connection.

Installing the `wayvnc` package already enables a systemd service (`wayvnc.service`) that runs
on boot and captures the live HDMI output. By default that service is password-protected with
a scheme the browser client (noVNC) cannot negotiate, so set it to accept unauthenticated
connections — the admin server is the only thing that reaches it, and the browser↔server link
is already authenticated:

```bash
sudo tee /etc/wayvnc/config >/dev/null <<'EOF'
use_relative_paths=true
address=::
enable_auth=false
EOF
sudo systemctl restart wayvnc.service
```

Verify it is listening:

```bash
systemctl is-active wayvnc.service     # expect: active
ss -tlnp | grep 5900                    # expect *:5900
```

> **Security note:** with `enable_auth=false`, port 5900 accepts unauthenticated VNC from any
> host on the LAN. The signage screen is public content, so this is acceptable on a trusted
> church/office network; keep these players off untrusted networks. See
> [docs/vnc-access.md](vnc-access.md) for the full rationale and alternatives.

After this, open the admin site, go to **Devices**, and click **Live View** on the device.

## Updating the Player

The Player automatically updates itself when new versions are available. You can also trigger manual updates:

### Automatic Updates (Default)

The Player polls the server every 30 seconds. When an update is available:
1. Player downloads the new binary
2. Swaps the binary file
3. Exits (systemd restarts it automatically)

### Manual Update Trigger

From the admin panel:
1. Navigate to Devices
2. Select your device
3. Click "Update to Version X.X.X"

### Rollback

If an update causes issues:
1. SSH into the device
2. Restore the backup binary:

```bash
sudo systemctl stop plainsight.service
cd /opt/plainsight
mv PlainSight.Player.bak PlainSight.Player
sudo systemctl start plainsight.service
```

## Troubleshooting

### Player Not Starting

```bash
# Check service status
sudo systemctl status plainsight.service

# View detailed logs
sudo journalctl -u plainsight.service -n 100

# Restart service
sudo systemctl restart plainsight.service
```

### SMB Mount Issues

```bash
# Check mount status
mount | grep signage

# Test manual mount
sudo mount -t cifs //SERVER_IP/signage /mnt/signage \
  -o username=pi,password=secure,vers=3.0

# Check network connectivity
ping SERVER_IP
```

### Display Issues

```bash
# Check if labwc is running
ps aux | grep labwc

# Restart labwc
killall labwc
labwc &
```

### No Network Connection

```bash
# Check network interface
ip addr show

# Check if Ethernet is connected
ethtool eth0

# Restart networking
sudo systemctl restart NetworkManager
```

## Performance Optimization

### Disable Unnecessary Services

```bash
sudo systemctl disable bluetooth.service
sudo systemctl disable avahi-daemon.service
```

### Enable Active Cooling

Ensure the active cooler is properly installed and running. Check temperature:

```bash
vcgencmd measure_temp
```

Temperature should stay below 70°C under load.

### Optimize Video Performance

Edit `/boot/firmware/config.txt`:

```ini
# GPU memory (256MB recommended for 4K)
gpu_mem=256

# Enable hardware video decode
dtoverlay=vc4-kms-v3d

# Overclock (optional, use with caution)
# over_voltage=6
# arm_freq=2400
```

## Security Hardening

### Change Default Password

```bash
passwd
```

### Configure Firewall

```bash
sudo apt install ufw
sudo ufw allow ssh
sudo ufw enable
```

### Disable SSH (After Setup)

```bash
sudo systemctl disable ssh
```

## Fleet Management

For managing multiple devices:

1. **Naming Convention**: Use consistent naming (e.g., `plainsight-sanctuary`, `plainsight-lobby`)
2. **Groups**: Organize devices by location or function in admin panel
3. **Canary Testing**: Test updates on one device before rolling out to all
4. **Monitoring**: Regularly check device status in admin panel

## Maintenance Schedule

- **Daily**: Check admin panel for device status
- **Weekly**: Review logs for errors
- **Monthly**: Check for OS updates
- **Quarterly**: Clean dust from cooler and case
- **Annually**: Replace MicroSD card (preventive)

## Support

For issues specific to Raspberry Pi setup, check:
- [Raspberry Pi Documentation](https://www.raspberrypi.com/documentation/)
- [labwc Documentation](https://github.com/labwc/labwc)
- PlainSight GitHub Issues
