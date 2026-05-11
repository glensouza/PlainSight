#!/bin/bash
set -e

# PlainSight Raspberry Pi Installation Script
# This script provisions a fresh Raspberry Pi for PlainSight digital plainsight

echo "================================================"
echo "  PlainSight Digital Signage Player Installer"
echo "================================================"
echo ""

# Check if running as root
if [ "$EUID" -eq 0 ]; then 
  echo "Please do not run as root. Run as pi user."
  exit 1
fi

# Read server IP
read -p "Enter the PlainSight Server IP address: " SERVER_IP
if [ -z "$SERVER_IP" ]; then
  echo "Server IP is required"
  exit 1
fi

# Read SMB details
echo ""
echo "SMB Share Configuration:"
read -p "Enter SMB Host (e.g., MyCloud.local): " SMB_HOST
read -p "Enter SMB Share and Path (e.g., corona/PlainSight): " SMB_PATH
read -p "Enter SMB username: " SMB_USER
read -sp "Enter SMB password: " SMB_PASSWORD
echo ""

if [ -z "$SMB_HOST" ] || [ -z "$SMB_PATH" ] || [ -z "$SMB_USER" ] || [ -z "$SMB_PASSWORD" ]; then
  echo "All SMB configuration fields are required"
  exit 1
fi

# 1. Update system
echo "Updating system packages..."
sudo apt update
sudo apt upgrade -y

# 2. Install dependencies
echo "Installing required dependencies..."
sudo apt install -y \
  labwc \
  wayland-protocols \
  cifs-utils \
  grim \
  swayidle \
  swaybg \
  wlopm \
  curl

# 3. Create directories
echo "Creating application and cache directories..."
sudo mkdir -p /opt/plainsight
sudo mkdir -p /mnt/plainsight
sudo mkdir -p /var/cache/plainsight/content
sudo mkdir -p /var/cache/plainsight/idle
sudo mkdir -p /etc/samba
sudo chown -R pi:pi /opt/plainsight
sudo chown -R pi:pi /var/cache/plainsight

# 4. Download Player Binary (Bootstrap)
echo ""
echo "WARNING: Downloading binary over HTTP (no integrity verification)"
echo "For production, use HTTPS and verify checksums/signatures"
echo ""
read -p "Continue? (y/n): " CONTINUE
if [ "$CONTINUE" != "y" ]; then
  echo "Installation aborted"
  exit 1
fi

echo "Downloading PlainSight Player..."
curl -L "http://${SERVER_IP}:8080/api/updates/latest/binary" -o /opt/plainsight/PlainSight.Player
chmod +x /opt/plainsight/PlainSight.Player

# 5. Create SMB credentials file
echo "Creating SMB credentials file..."
sudo bash -c "cat > /etc/samba/plainsight-credentials << EOF
username=${SMB_USER}
password=${SMB_PASSWORD}
EOF"
sudo chmod 600 /etc/samba/plainsight-credentials

# 6. Configure systemd units
echo "Configuring systemd services..."

# SMB mount (stays as system unit)
sudo bash -c "cat > /etc/systemd/system/mnt-plainsight.mount << EOF
[Unit]
Description=Mount Remote Signage Assets
After=network-online.target

[Mount]
What=//${SMB_HOST}/${SMB_PATH}
Where=/mnt/plainsight
Type=cifs
Options=credentials=/etc/samba/plainsight-credentials,ro,vers=3.0

[Install]
WantedBy=multi-user.target
EOF"

# Automount (stays as system unit)
sudo bash -c "cat > /etc/systemd/system/mnt-plainsight.automount << EOF
[Unit]
Description=Automount Signage Share

[Automount]
Where=/mnt/plainsight
TimeoutIdleSec=0

[Install]
WantedBy=multi-user.target
EOF"

# PlainSight player service (User Unit)
mkdir -p ~/.config/systemd/user
cat > ~/.config/systemd/user/plainsight.service << EOF
[Unit]
Description=PlainSight Digital Signage Player
After=network-online.target
# We don't depend on mnt-plainsight.mount here because it's a system unit,
# but the player will handle the path being missing gracefully.

[Service]
Type=simple
WorkingDirectory=/opt/plainsight
ExecStart=/opt/plainsight/PlainSight.Player
Restart=always
RestartSec=3
Environment=DISPLAY=:0
Environment=WAYLAND_DISPLAY=wayland-1
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1
Environment=ServerUrl=http://${SERVER_IP}:8080

[Install]
WantedBy=default.target
EOF

# 6. Configure labwc
echo "Configuring labwc window manager..."
mkdir -p ~/.config/labwc

cat > ~/.config/labwc/rc.xml << 'EOF'
<labwc_config>
  <windowRules>
    <windowRule identifier="PlainSight.Player">
      <action name="ToggleFullscreen" />
      <action name="KeepAbove" />
    </windowRule>
  </windowRules>
</labwc_config>
EOF

cat > ~/.config/labwc/autostart << 'EOF'
#!/bin/bash
# Solid black desktop so there is no flash between Plymouth exit and the player window appearing
swaybg -c 000000 &

# Disable screen sleep/power saving
swayidle -w timeout 31536000 'wlopm --off \*' resume 'wlopm --on \*' &
EOF

chmod +x ~/.config/labwc/autostart

# 7. Configure silent boot for signage displays
echo "Configuring silent boot..."

# Determine boot partition path (Bookworm uses /boot/firmware; older Pi OS uses /boot)
if [ -d "/boot/firmware" ]; then
  BOOT_DIR="/boot/firmware"
else
  BOOT_DIR="/boot"
fi

# Disable the GPU firmware rainbow splash shown before the kernel loads
CONFIG_FILE="$BOOT_DIR/config.txt"
if grep -q "^disable_splash" "$CONFIG_FILE"; then
  sudo sed -i 's/^disable_splash=.*/disable_splash=1/' "$CONFIG_FILE"
elif grep -q "^#disable_splash" "$CONFIG_FILE"; then
  sudo sed -i 's/^#disable_splash.*/disable_splash=1/' "$CONFIG_FILE"
else
  printf '\n# Suppress firmware boot splash\ndisable_splash=1\n' | sudo tee -a "$CONFIG_FILE" > /dev/null
fi

# Install Plymouth directly so we control the package before touching cmdline.txt.
# raspi-config do_boot_splash fails on a fresh image because Plymouth is not yet
# installed when it runs, which causes set -e to abort the script prematurely.
echo "Installing Plymouth (this may take a minute)..."
sudo apt-get install -y plymouth plymouth-themes

# Remove console=tty1 so the kernel and systemd write to the serial port only,
# not the HDMI display. Plymouth starts too late on Pi OS (no initramfs by
# default) to reliably intercept this output, so removing the binding is the
# only way to guarantee a clean screen. The autologin getty and labwc both use
# tty1 directly and are unaffected by this change.
# quiet + splash are added here because raspi-config do_boot_splash is skipped.
CMDLINE_FILE="$BOOT_DIR/cmdline.txt"
CMDLINE=$(cat "$CMDLINE_FILE")
CMDLINE=$(echo "$CMDLINE" | sed 's/\bconsole=tty[0-9]*\b//g' | sed 's/  */ /g' | sed 's/^ //')
for PARAM in quiet splash loglevel=0 logo.nologo vt.global_cursor_default=0; do
  if ! echo "$CMDLINE" | grep -qw "$PARAM"; then
    CMDLINE="$CMDLINE $PARAM"
  fi
done
echo "$CMDLINE" | sudo tee "$CMDLINE_FILE" > /dev/null

# Create PlainSight Plymouth theme — solid black screen, no distracting animation
THEME_DIR="/usr/share/plymouth/themes/plainsight"
sudo mkdir -p "$THEME_DIR"

sudo tee "$THEME_DIR/plainsight.plymouth" > /dev/null << 'EOF'
[Plymouth Theme]
Name=PlainSight
Description=PlainSight Digital Signage
ModuleName=script

[script]
ImageDir=/usr/share/plymouth/themes/plainsight
ScriptFile=/usr/share/plymouth/themes/plainsight/plainsight.script
EOF

sudo tee "$THEME_DIR/plainsight.script" > /dev/null << 'EOF'
Window.SetBackgroundTopColor(0.0, 0.0, 0.0);
Window.SetBackgroundBottomColor(0.0, 0.0, 0.0);
EOF

echo "Applying PlainSight Plymouth theme (rebuilding initramfs, please wait)..."
sudo plymouth-set-default-theme -R plainsight

# 8. Enable services
echo "Configuring boot target, autologin, and enabling services..."
sudo systemctl set-default graphical.target
sudo raspi-config nonint do_boot_behaviour B2

# Ensure labwc starts on login
if ! grep -q "exec labwc" ~/.bash_profile 2>/dev/null; then
  echo 'if [ -z "$DISPLAY" ] && [ "$(tty)" = "/dev/tty1" ]; then exec labwc; fi' >> ~/.bash_profile
fi

sudo systemctl daemon-reload
sudo systemctl enable mnt-plainsight.automount
systemctl --user daemon-reload
systemctl --user enable plainsight.service
# Allow the player user service to start at boot without waiting for interactive login
sudo loginctl enable-linger pi
# Grant passwordless sudo so remote management tools can run privileged commands
echo 'pi ALL=(ALL) NOPASSWD: ALL' | sudo tee /etc/sudoers.d/010_pi-nopasswd > /dev/null
sudo chmod 440 /etc/sudoers.d/010_pi-nopasswd

echo ""
echo "================================================"
echo "Installation Complete!"
echo "================================================"
echo ""
echo "The system will now reboot."
echo "After reboot, the PlainSight Player will start automatically."
echo ""
read -p "Press Enter to reboot now..."

sudo reboot
