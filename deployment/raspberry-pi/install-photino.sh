#!/bin/bash
set -e

# PlainSight Raspberry Pi Installation Script - Photino Player
# This script provisions a fresh Raspberry Pi for PlainSight digital signage with Photino player

echo "===================================================="
echo "  PlainSight Digital Signage Photino Player Setup"
echo "===================================================="
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

# Read SMB credentials
echo ""
echo "SMB Share Credentials:"
read -p "Enter SMB username: " SMB_USER
read -sp "Enter SMB password: " SMB_PASSWORD
echo ""

if [ -z "$SMB_USER" ] || [ -z "$SMB_PASSWORD" ]; then
  echo "SMB credentials are required"
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
  curl \
  libgtk-3-0 \
  libwebkit2gtk-4.1-0

# 3. Create directories
echo "Creating application directories..."
sudo mkdir -p /opt/signage
sudo mkdir -p /mnt/signage
sudo mkdir -p /etc/samba
sudo chown pi:pi /opt/signage

# 4. Download Photino Player Binary
echo ""
echo "WARNING: Downloading binary over HTTP (no integrity verification)"
echo "For production, use HTTPS and verify checksums/signatures"
echo ""
read -p "Continue? (y/n): " CONTINUE
if [ "$CONTINUE" != "y" ]; then
  echo "Installation aborted"
  exit 1
fi

echo "Downloading PlainSight Photino Player..."
curl -L "http://${SERVER_IP}:8080/api/updates/latest/photino-binary" -o /opt/signage/Signage.Player.Photino
chmod +x /opt/signage/Signage.Player.Photino

# 5. Create SMB credentials file
echo "Creating SMB credentials file..."
sudo bash -c "cat > /etc/samba/signage-credentials << EOF
username=${SMB_USER}
password=${SMB_PASSWORD}
EOF"
sudo chmod 600 /etc/samba/signage-credentials

# 6. Configure systemd units
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
Options=credentials=/etc/samba/signage-credentials,ro,vers=3.0

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

# Photino Signage service
sudo bash -c "cat > /etc/systemd/system/signage-photino.service << EOF
[Unit]
Description=PlainSight Digital Signage Player (Photino)
After=network-online.target mnt-signage.mount
Wants=mnt-signage.mount

[Service]
Type=simple
User=pi
WorkingDirectory=/opt/signage
ExecStart=/opt/signage/Signage.Player.Photino
Restart=always
RestartSec=3
Environment=DISPLAY=:0
Environment=WAYLAND_DISPLAY=wayland-1
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1
Environment=ServerUrl=http://${SERVER_IP}:8080
Environment=ContentPath=/mnt/signage/content

[Install]
WantedBy=graphical.target
EOF"

# 6. Configure labwc
echo "Configuring labwc window manager..."
mkdir -p ~/.config/labwc

cat > ~/.config/labwc/rc.xml << 'EOF'
<labwc_config>
  <windowRules>
    <windowRule identifier="Signage.Player.Photino">
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

# Start PlainSight Photino Player
/opt/signage/Signage.Player.Photino &
EOF

chmod +x ~/.config/labwc/autostart

# 7. Enable services
echo "Enabling systemd services..."
sudo systemctl daemon-reload
sudo systemctl enable mnt-signage.automount
sudo systemctl enable signage-photino.service

echo ""
echo "===================================================="
echo "Installation Complete!"
echo "===================================================="
echo ""
echo "The system will now reboot."
echo "After reboot, the PlainSight Photino Player will start automatically."
echo ""
read -p "Press Enter to reboot now..."

sudo reboot
