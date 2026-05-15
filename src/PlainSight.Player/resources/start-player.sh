#!/bin/bash
set -e

# PlainSight Player Display Server Startup Script
# Initializes X11/Openbox display stack for Raspberry Pi fullscreen kiosk display.
#
# Dependencies (must be installed before deployment):
#   - xvfb: virtual X11 framebuffer
#   - openbox: minimal window manager
#   - chromium: web browser
#   - unclutter: cursor hiding tool
#
# Install via: sudo apt-get install -y xvfb openbox chromium unclutter

export DISPLAY=:99
export XAUTHORITY=/home/pi/.Xauthority

# Terminate any processes from previous runs to ensure clean startup
pkill -f Xvfb || true
pkill -f openbox || true
pkill -f chromium || true
sleep 1

# Start Xvfb virtual framebuffer on display :99
# Resolution: 1920x1080, Color depth: 24-bit
Xvfb :99 -screen 0 1920x1080x24 &
XVFB_PID=$!
sleep 2

# Verify Xvfb is actually running before proceeding
if ! kill -0 $XVFB_PID 2>/dev/null; then
    echo "ERROR: Xvfb failed to start (PID $XVFB_PID)" >&2
    exit 1
fi

# Start Openbox minimal window manager (no panels, decorations, or overhead)
openbox --replace &
OPENBOX_PID=$!
sleep 2

# Hide cursor after 1 second of inactivity using unclutter
unclutter -display :99 -idle 1 -root &

# Start Chromium in fullscreen kiosk mode (--kiosk enables true fullscreen via X11)
# Points to the local .NET Kestrel server on port 5555
# Force X11 rendering backend to ensure content renders to Xvfb (not Wayland)
chromium --no-first-run --kiosk --ozone-platform=x11 http://localhost:5555/player &
CHROMIUM_PID=$!

# Wait for Chromium to exit; systemd will restart on signal/crash via Restart=always
wait $CHROMIUM_PID

# Clean up remaining display server processes on exit
kill $XVFB_PID $OPENBOX_PID 2>/dev/null || true
