#!/bin/bash
set -e

# PlainSight Player Display Startup Script
# Launches Chromium in kiosk mode via labwc/XWayland on the physical HDMI screen.
#
# labwc (Wayland compositor) must be running and have started XWayland before
# this script is called. DISPLAY=:0 and WAYLAND_DISPLAY are set by the systemd
# unit so Chromium routes output through XWayland → labwc → physical HDMI.
#
# Dependencies: chromium, grim
# Install: sudo apt-get install -y chromium grim

# Kill any stale Chromium from a previous run
pkill -f chromium || true

# Wait up to 30 s for labwc to expose the XWayland socket
WAIT=0
while [ ! -S /tmp/.X11-unix/X0 ] && [ "$WAIT" -lt 30 ]; do
    sleep 1
    WAIT=$((WAIT + 1))
done

if [ ! -S /tmp/.X11-unix/X0 ]; then
    echo "ERROR: XWayland socket /tmp/.X11-unix/X0 not available after 30 s" >&2
    echo "Is labwc running? Check: systemctl --user status labwc or ~/.bash_profile" >&2
    exit 1
fi

# Hide the X11 cursor (parked at 0,0 with no mouse on a kiosk display).
# Runs on DISPLAY=:0 (XWayland) so it hides the compositor-level cursor.
unclutter -display :0 -idle 0.5 -root &

# Chromium renders via X11 backend → XWayland → labwc → physical HDMI.
# DISPLAY=:0 and WAYLAND_DISPLAY=wayland-0 are inherited from the systemd unit.
chromium \
    --no-first-run \
    --kiosk \
    --ozone-platform=x11 \
    http://localhost:5555/player &
CHROMIUM_PID=$!

wait $CHROMIUM_PID
