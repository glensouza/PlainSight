# Boot Splash Customization

PlainSight replaces every stage of the default Raspberry Pi boot sequence — firmware rainbow, kernel messages, and the unstyled loading gap — with a clean, branded experience.

## How it works

Boot visibility is split across three layers, each covering a different window of time:

| Layer | When visible | What shows by default |
|---|---|---|
| **Plymouth** | Kernel → systemd init | "PlainSight / Digital Signage" text on dark gradient |
| **labwc desktop** | Login → player window appears | Custom splash PNG, or solid dark `#0a0a14` |
| **Player loading screen** | Player start → first content frame | Branded gradient with "PlainSight" heading |

`install.sh` configures all three automatically. No manual steps are required for the defaults.

## Adding a custom splash image

The labwc desktop layer supports any 1920×1080 PNG. Drop your branded image at:

```
/opt/plainsight/splash.png
```

`swaybg` will stretch it to fill the screen. If the file is absent the fallback colour (`#0a0a14`) is used instead. You can copy the image during provisioning or push it via the SMB share:

```bash
# From your workstation — copy over SSH
scp branding/splash.png pi@plainsight-player-01.local:/opt/plainsight/splash.png
```

No reboot is required; the change takes effect the next time the Pi powers on.

## Plymouth splash (text-based)

The Plymouth theme (`/usr/share/plymouth/themes/plainsight/`) renders two lines of text using the `script` module — no PNG file is required, and it works on Raspberry Pi OS Lite which has no initramfs.

To change the text, edit the script on the device and rebuild the theme:

```bash
sudo nano /usr/share/plymouth/themes/plainsight/plainsight.script
sudo plymouth-set-default-theme -R plainsight
```

The script variables that control appearance:

| Variable | Purpose |
|---|---|
| `Window.SetBackgroundTopColor(r, g, b)` | Top colour of the gradient (0–1 per channel) |
| `Window.SetBackgroundBottomColor(r, g, b)` | Bottom colour of the gradient |
| `Image.Text("…", r, g, b)` | Render a line of text in the given colour |

## Kernel message suppression

`install.sh` adds the following parameters to `/boot/firmware/cmdline.txt`:

```
quiet splash loglevel=0 logo.nologo vt.global_cursor_default=0
```

It also removes `console=tty1` so the kernel writes only to the serial port, not the HDMI display. Together these ensure no boot text is ever visible on screen.

The firmware rainbow splash is disabled separately in `/boot/firmware/config.txt`:

```
disable_splash=1
```

## Troubleshooting

**Plymouth does not appear**
Raspberry Pi OS Lite does not build an initramfs by default, so Plymouth runs for only a brief moment before the desktop starts. This is expected — the branded labwc background takes over immediately after.

**Splash image not showing**
Verify the file exists and is readable:
```bash
ls -lh /opt/plainsight/splash.png
```
Also confirm `swaybg` is installed:
```bash
which swaybg
```

**Boot messages still visible**
Check that `cmdline.txt` contains all required parameters and has no trailing newline (it must be a single line):
```bash
cat /boot/firmware/cmdline.txt
```
