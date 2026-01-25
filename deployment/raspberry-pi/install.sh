#!/bin/bash
set -e

# PlainSight Raspberry Pi Installation Script
# This script provisions a fresh Raspberry Pi for PlainSight digital signage

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
  wlopm \
  curl

# 3. Create directories
echo "Creating application directories..."
sudo mkdir -p /opt/signage
sudo mkdir -p /mnt/signage
sudo chown pi:pi /opt/signage

# 4. Download Player Binary (Bootstrap)
echo "Downloading PlainSight Player..."
curl -L "http://${SERVER_IP}:8080/api/updates/latest/binary" -o /opt/signage/Signage.Player
chmod +x /opt/signage/Signage.Player

# 5. Configure systemd units
echo "Configuring systemd services..."

# SMB mount
sudo bash -c "cat > /etc/systemd/system/mnt-signage.mount << EOF
[Unit]
Description=Mount Remote Signage Assets
After=network-online.target

[Mount]
What=//${SERVER_IP}/signage
Where=/mnt/signage
Type=cifs
Options=username=pi,password=secure,ro,vers=3.0

[Install]
WantedBy=multi-user.target
EOF"

# Automount
sudo bash -c "cat > /etc/systemd/system/mnt-signage.automount << EOF
[Unit]
Description=Automount Signage Share

[Automount]
Where=/mnt/signage
TimeoutIdleSec=0

[Install]
WantedBy=multi-user.target
EOF"

# Signage service
sudo bash -c "cat > /etc/systemd/system/signage.service << EOF
[Unit]
Description=PlainSight Digital Signage Player
After=network-online.target mnt-signage.mount
Wants=mnt-signage.mount

[Service]
Type=simple
User=pi
WorkingDirectory=/opt/signage
ExecStart=/opt/signage/Signage.Player
Restart=always
RestartSec=3
Environment=DISPLAY=:0
Environment=WAYLAND_DISPLAY=wayland-1
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1
Environment=ServerUrl=http://${SERVER_IP}:8080

[Install]
WantedBy=graphical.target
EOF"

# 6. Configure labwc
echo "Configuring labwc window manager..."
mkdir -p ~/.config/labwc

cat > ~/.config/labwc/rc.xml << 'EOF'
<labwc_config>
  <windowRules>
    <windowRule identifier="Signage.Player">
      <action name="ToggleFullscreen" />
      <action name="KeepAbove" />
    </windowRule>
  </windowRules>
</labwc_config>
EOF

cat > ~/.config/labwc/autostart << 'EOF'
#!/bin/bash
# Disable screen sleep/power saving
swayidle -w timeout 31536000 'wlopm --off \*' resume 'wlopm --on \*' &

# Start PlainSight Player
/opt/signage/Signage.Player &
EOF

chmod +x ~/.config/labwc/autostart

# 7. Enable services
echo "Enabling systemd services..."
sudo systemctl daemon-reload
sudo systemctl enable mnt-signage.automount
sudo systemctl enable signage.service

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
