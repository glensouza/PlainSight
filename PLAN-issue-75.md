# Plan — Issue #75: Custom splash screen on the Pi

> This file is a temporary planning artifact. The **last step of this PR
> deletes it**, so it must not survive into `main`.

## Goal

Replace the solid `#0a0a14` swaybg fill that appears between labwc starting
and Chromium painting its first frame with a branded **1920×1080 PNG**
containing the PlainSight logo centered and the Pi's hostname in the
bottom-right corner. The same image must also be the first thing Chromium
displays — for **10 seconds** — before any playlist begins playing.

Reference: existing labwc autostart already calls
`swaybg -i /opt/plainsight/splash.png -m fill` (see
`deployment/raspberry-pi/install.sh:166-172`), but the PNG itself is never
created today, so the `else` branch (`swaybg -c 0a0a14`) is what every Pi
actually shows.

---

## High-level approach

1. **Generate the splash PNG inside the C# player at startup.** The device
   name is per-Pi so a static asset can't carry it. `System.Drawing.Common`
   is already referenced (`PlainSight.Player.csproj:13`) and runs on
   linux-arm64 with `libgdiplus`. The player runs as `pi` and `/opt/plainsight`
   is already `chown pi:pi` (install.sh:65), so it has write permission.
2. **Show the same PNG in the Chromium player for 10 s on cold start.**
   Wire it into `wwwroot/index.html`'s loading screen — replace the current
   gradient + "PlainSight / Loading content..." block with an `<img>` that
   loads `/splash.png` and hold it on screen for a minimum of 10 s before
   the first playlist item plays.
3. **Expose `/splash.png`** from the player's embedded Kestrel server so the
   browser can fetch the same file `swaybg` is using.
4. **Keep `install.sh` untouched apart from a comment refresh** — the
   labwc/swaybg wiring is already correct; the only thing it was missing
   was the file, which step 1 now produces.

No EF migration, no DB change, no server-side change.

---

## Step-by-step

### 1. Add `SplashGeneratorService` (player, new file)

- New file: `src/PlainSight.Player/Services/SplashGeneratorService.cs`.
- `IHostedService` that runs **before** `KioskService` (register first in
  `Program.cs`; `IHostedService`s start in registration order).
- `StartAsync` logic:
  - Resolve splash path from config: `SplashPath` →
    default `/opt/plainsight/splash.png`.
  - If `OperatingSystem.IsLinux()` is `false`, log "skipping on non-Linux"
    and return — keeps Windows dev runs from crashing on
    `System.Drawing.Common`.
  - If the file already exists **and** the embedded "splash version" marker
    (a sibling `splash.version` text file) matches the current build, skip
    regeneration. Otherwise generate.
  - Draw a 1920×1080 ARGB bitmap:
    - Fill background with the existing `#0a0a14` so swaybg's fallback
      and the real splash look identical.
    - Center the PlainSight logo (concentric circles, matching
      `wwwroot/favicon.svg` — three circles + center dot, scaled up
      ~10×).
    - Below the logo, large "PlainSight" wordmark in white, then a smaller
      "Digital Signage" subtitle in muted gray (mirrors the existing
      `#loading` block in `wwwroot/index.html:166-170`).
    - Bottom-right corner: `Environment.MachineName` in 32 pt muted white
      with 32 px right/bottom padding (mirrors `.loading-device-name`
      styling).
  - Save as PNG to a temp path next to the target, then `File.Move` with
    overwrite to make the swap atomic (prevents swaybg from reading a
    half-written file on the next boot).
  - Write the `splash.version` marker.
- Style: explicit types throughout, primary constructor where it fits,
  `this.` on all instance access, no underscores, `Lock` not `object`,
  collection expressions, brace everything. No `var`.

**Why `System.Drawing.Common` over SkiaSharp:** already in the project,
already runs in CI for ARM64 publishes, and `libgdiplus` is a transitive
dependency of nothing we'd lose by adding it through apt. We'll add
`libgdiplus` to the apt install list in `install.sh`.

### 2. Wire the service into `Program.cs`

- File: `src/PlainSight.Player/Program.cs`.
- Read `SplashPath` from `builder.Configuration` with the same
  `MediaPathResolver.Resolve` pattern used for other paths (~line 18-25).
- Register the service: `builder.Services.AddHostedService<SplashGeneratorService>();`
  — insert it **above** the existing `AddHostedService<KioskService>()` call
  (line 74) so the splash file exists before Chromium launches.
- Add a minimal route handler that serves the generated PNG so the browser
  can show it:
  ```
  app.MapGet("/splash.png", (IConfiguration cfg) => {
      string path = cfg["SplashPath"] ?? "/opt/plainsight/splash.png";
      return File.Exists(path) ? Results.File(path, "image/png") : Results.NotFound();
  });
  ```
  Insert near the existing `/content/{filename}` handler (~line 118).

### 3. Update `wwwroot/index.html` for the 10-second cold-start splash

- File: `src/PlainSight.Player/wwwroot/index.html`.
- Replace the current `#loading` div body (lines 166-170) — drop the
  gradient title/subtitle/device-name spans, replace with:
  ```
  <div id="loading">
      <img id="splash-img" src="/splash.png" alt="">
  </div>
  ```
- Update the `#loading` CSS so the image fills the viewport with
  `object-fit: cover` (or `fill` to match swaybg's `-m fill`). Keep the
  fallback gradient on `#loading` itself so a missing splash still shows
  something dark instead of white flash.
- JS change in the `<script>` block:
  - Add a module-scoped `const COLD_START_SPLASH_MS = 10_000;` and a
    timestamp captured at script load: `const bootStartedAt = performance.now();`.
  - Wrap the existing `playMedia(0)` call inside `loadPlaylistFromServer`
    (line 474) in a helper that waits until
    `performance.now() - bootStartedAt >= COLD_START_SPLASH_MS` before
    starting playback. The first non-empty playlist response after that
    threshold triggers `playMedia(0)`; earlier responses just cache the
    playlist and keep the splash visible.
  - The existing `loadingEl.style.display = 'none'` (set on successful
    media `play`) already takes care of dismissing the splash once the
    first item paints.
- Confirm `#loading` z-index is above `#idle-screen` so the splash wins
  during the 10-second hold even if the idle screen would otherwise show.

### 4. Make `install.sh` self-sufficient for the splash

- File: `deployment/raspberry-pi/install.sh`.
- Add `libgdiplus` to the apt package list (after `unclutter` ~line 55) so
  `System.Drawing.Common` works on a fresh Bookworm image.
- Refresh the autostart comment (lines 166-172) — drop "Replace
  /opt/plainsight/splash.png with your own…" since the player now
  generates it. Update to note that the file is auto-generated on first
  run; users replacing it should drop in a 1920×1080 PNG before first
  boot.
- Leave the Plymouth theme and swaybg wiring untouched.

### 5. Manual verification checklist

Run on a real Pi (or note in PR that this can't be tested in CI):

- [ ] Fresh boot shows the new branded splash (logo + hostname) instead of
      flat `#0a0a14`.
- [ ] Splash persists through labwc → Chromium handoff without flicker.
- [ ] Chromium displays the same image for ~10 seconds before the first
      playlist item plays.
- [ ] Once content starts, the splash is no longer visible (no stuck
      `#loading` overlay).
- [ ] If the Pi has no playlist, the existing idle screen still appears
      after the 10 s hold.
- [ ] Replacing `/opt/plainsight/splash.png` by hand and rebooting picks
      up the override (regeneration check via `splash.version`).
- [ ] `dotnet build` and `dotnet format` clean on Windows.

### 6. PR hygiene

- All commits target branch `feat/custom-splash-screen-75`.
- PR body uses `Closes #75` so merging closes the issue automatically.
- PR opened as **draft** per request; user flips to ready when satisfied.
- The `bump-minor.yml` workflow will bump `version.txt` on PR open — no
  manual version edit needed.

### 7. Delete this plan file

The final commit in the PR removes `PLAN-issue-75.md` so it never lands
on `main`. (This step exists because the user asked for it; the plan is
a working artifact, not project documentation.)

---

## Open questions / things to confirm with the user

- **Logo style fidelity** — I'll mirror the favicon (three concentric
  circles + cyan center dot). If a specific raster logo asset exists
  somewhere else in the repo or in a brand folder, swap that in instead
  of drawing primitives.
- **Hostname vs. friendly device name** — the heartbeat API uses
  `DeviceId` and `Device.Name` server-side, but the player's
  `Environment.MachineName` is the only identifier available locally
  before the heartbeat round-trip. Using `MachineName` keeps splash
  generation offline-safe; switching to the server's friendly name would
  require a network call before the first frame, which defeats the
  purpose of the splash.
- **10 s exact vs. "at least 10 s"** — I'm implementing "at least 10 s
  from process start until first playlist item paints." If the playlist
  is slow to fetch, the splash naturally extends. If the user wants a
  hard 10 s ceiling, say so and I'll cap it.
