# PlainSight Roadmap

Implementation plan for completing incomplete features and improving the app. Issues are worked one branch at a time. Check off each item as it ships to `main`.

---

## Phase 1 — Cleanup & Consolidation

- [ ] **[#12](https://github.com/glensouza/PlainSight/issues/12) Remove Signage.Player.Photino; add Chromium kiosk player**
  Remove the Photino project (net8.0 pin, niche dependency). Replace its display functionality inside `Signage.Player` using an embedded Kestrel HTTP server + `chromium-browser --kiosk`. Port the HTML5 video/playlist page as an embedded resource. Removes the only net8.0 project from the solution.

---

## Phase 2 — Complete Incomplete Features

- [ ] **[#13](https://github.com/glensouza/PlainSight/issues/13) Database-driven version management and canary deployments**
  `VersionService` is hardcoded to `"1.0.0"`. Add `DeviceGroupVersion` table, update `VersionService` to query it, rebuild the Versions Blazor page against real data, and add a version binary upload endpoint.

- [ ] **[#14](https://github.com/glensouza/PlainSight/issues/14) Complete screenshot pipeline: player upload, server storage, admin preview**
  Player captures screen but never uploads it. Complete the full round-trip: player POSTs PNG to server, server stores it on SMB share, Devices page shows a preview modal. *(Depends on #12)*

- [ ] **[#15](https://github.com/glensouza/PlainSight/issues/15) Complete WebsiteRecorder: PuppeteerSharp screencast + FFmpeg MP4 encoding**
  `ConvertUrlToVideoAsync` opens the page and returns without writing any video. Implement frame capture via `Page.StartScreencastAsync()` piped into an FFmpeg subprocess. Add FFmpeg to the Docker image.

---

## Phase 3 — Security

- [ ] **[#16](https://github.com/glensouza/PlainSight/issues/16) Admin UI authentication (cookie-based login)**
  All pages and API endpoints are unauthenticated. Add cookie-based login with a bcrypt-hashed credential in config. Protect all Blazor pages and controllers except the device heartbeat and screenshot upload endpoints.

- [ ] **[#17](https://github.com/glensouza/PlainSight/issues/17) Device API key authentication**
  Any device can spoof any `deviceId`. First heartbeat registers the device and returns a generated API key; player persists it locally. All subsequent heartbeats require `X-Api-Key` header. Admin can reset a key from the Devices page. *(Depends on #16)*

---

## Phase 4 — Reliability

- [ ] **[#18](https://github.com/glensouza/PlainSight/issues/18) Local content cache with SMB fallback**
  Players stream directly from the SMB mount — a network drop means a blank screen. Add `CacheService` that syncs content to `/var/cache/plainsight/content/` and serves from cache when SMB is unavailable. Kestrel serves from cache, not from the mount directly. *(Depends on #12)*

---

## Phase 5 — New Features

- [ ] **[#19](https://github.com/glensouza/PlainSight/issues/19) Content scheduling: time-based playlist assignment**
  No way to schedule which playlist plays when. Add a `Schedule` table (playlist, device group, days of week, time range, priority). `ScheduleService` returns the active playlist for a group at the current time. Heartbeat response includes the active file list; player switches content when it changes. New Schedule admin page. *(Depends on #13)*

- [ ] **[#20](https://github.com/glensouza/PlainSight/issues/20) Device offline alerts via email**
  Devices go offline silently. Add `DeviceMonitorService` (background service) that detects devices missing for >5 minutes and sends an email alert. Sends a recovery email when the device comes back. Configurable threshold and SMTP settings via `appsettings.json` / environment variables.

- [ ] **[#21](https://github.com/glensouza/PlainSight/issues/21) Periodic auto-screenshot with screenshot history**
  Screenshots are on-demand only. Add automatic capture every N minutes from all online devices. Retain last N screenshots per device in a `DeviceScreenshot` table. Devices page shows a thumbnail strip of recent captures. *(Depends on #14)*

- [ ] **[#22](https://github.com/glensouza/PlainSight/issues/22) Bulk device actions: mass screenshot, group assignment, version assignment**
  Devices page only supports per-device actions. Add multi-select checkboxes and a floating action bar for bulk screenshot requests, group moves, and version assignments. Promote device groups to a first-class `DeviceGroup` table (dropdown, not freetext). *(Depends on #13, #14)*

---

## Dependency map

```
#12 (Photino removal)
 ├── #14 (screenshot pipeline)
 │    └── #21 (auto-screenshot history)
 └── #18 (local cache)

#13 (version management)
 ├── #19 (scheduling)
 └── #22 (bulk actions) ← also depends on #14

#16 (admin auth)
 └── #17 (device API keys)

#15 (WebsiteRecorder)   — no dependents
#20 (offline alerts)    — no dependents
```

---

## Suggested work order

| Order | Issue | Reason |
|---|---|---|
| 1 | #12 | Unblocks #14 and #18; removes net8.0 from solution |
| 2 | #13 | Unblocks #19 and #22; standalone |
| 3 | #15 | Standalone; completes a visible broken feature |
| 4 | #16 | Standalone; should land before the app is exposed externally |
| 5 | #14 | Depends on #12 |
| 6 | #17 | Depends on #16 |
| 7 | #18 | Depends on #12 |
| 8 | #20 | Standalone; no blockers |
| 9 | #19 | Depends on #13 |
| 10 | #21 | Depends on #14 |
| 11 | #22 | Depends on #13 and #14 |
