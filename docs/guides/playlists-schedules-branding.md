# Playlists, Schedules & Branding

How to program what plays on your signage screens and when.

## Quick Path

1. **Upload content** to the Content Library.
2. **Create a playlist** → add content items in order.
3. **Create a schedule** → assign the playlist, set days/times, pick target groups.
4. (Optional) Add a **branding interstitial** to play between playlist loop passes.
5. Devices begin playing the schedule on their next heartbeat (within 30 s).

## Playlists

Navigate to **Playlists** from the sidebar.

### Creating a Playlist
- Click **Create New Playlist**. 
- Enter a name and optional description.
- Toggle **Active** to make it available for schedules.
- Click **Save Playlist**.

### Managing Content
- Click **Manage Content** on a playlist card.
- **Current Items**: Drag to reorder (grip handle on each row). Click the trash icon to remove.
- **Add Content**: Lists all content NOT already in the playlist. Click **Add** on any item.
- **Add Announcement**: Lists all announcements NOT already in the playlist. Click **Add** on any item.
- Click **Preview** to see how the playlist will look to viewers.
- Changes invalidate the schedule cache immediately.

### Notes
- Each item shows its duration badge. Set an override duration per item in the Content page's Rename modal.
- An announcement's media is expanded, in order, at heartbeat/preview time — you don't need to add its items individually.
- Reordering renumbers all items 1-indexed and updates the `UpdatedAt` timestamp.

## Announcements

Navigate to **Announcements** from the sidebar. An announcement groups related media (e.g. an event's image and video) under one title, so it can be added to a playlist as a single item that plays its media back-to-back, in order.

### Creating an Announcement
- Click **Create New Announcement**.
- Enter a **Title** and optional **Description**.
- Optionally set an **Event Date** and **Expires At**.
- Click **Save Announcement**.

### Managing Media
- Click **Manage Media** on an announcement card.
- **Current Media**: Reorder with the up/down arrows. Click the trash icon to remove.
- **Add Content**: Lists all content NOT already in the announcement. Click **Add** on any item.

Once created, add the announcement to a playlist from that playlist's **Add Announcement** list (see above).

## Schedules

Navigate to **Schedules** from the sidebar.

### Creating a Schedule
- Click **New Schedule**. The modal has these fields:

| Field | Description |
|---|---|
| **Name** | e.g. "Sunday Morning Service" |
| **Priority** | Higher number wins when schedules overlap. |
| **Target Groups** | Which device groups receive this schedule. "All Groups" = global. |
| **Playlist** | Which playlist to play. |
| **Type** | Recurring (weekly) or One-time (specific date). |
| **Days** | Recurring only: checkboxes for Mon–Sun. |
| **Start/End Time** | Time window when the schedule is active. |
| **Auto-screenshot** | Toggle to capture screenshots on content change. When enabled, set **Number of shots** (1–20) and **Seconds between shots** (1–300). |

### Active Schedule Resolution
- Multiple schedules can be active at once. The one with the **highest priority** wins.
- Schedules with **target groups** matching the device override global schedules (All Groups).
- The server evaluates this on every heartbeat and delivers the winning playlist.
- The in-memory schedule cache has a TTL of `Schedules:CacheSeconds` (default 15 s).

### Status Toggle
- Each schedule has an Active/Inactive toggle switch. Inactive schedules are ignored.

## Branding

Branding videos play as interstitials between playlist loop passes. Every complete pass through the playlist triggers one branding video insertion before the next pass begins.

### Access
- Navigate to **Branding** from the sidebar (Admin only).

### Adding Branding Videos
- **Direct Upload**: Pick a video file and click **Upload**.
- **Add from Idle Library**: Select an idle file and click **Add to Branding**.

### Setting a Default
- Videos marked as **Default** (blue badge) play when no branding schedule matches.
- Click **Set as Default** on any video to make it the default.

### Branding Schedules
- Assign different branding videos to different time windows and device groups.
- Click **New** in the Branding Schedules table.
- Select a group, video, time range, and days of the week.
- If no schedule matches, the default branding video plays.

### Preview
- Click the **Preview** button on any branding video to watch it.

## Auto-Screenshot Burst

When a schedule change is detected for a device (new schedule ID in heartbeat), and **Auto-screenshot** is enabled on the active schedule, the server triggers a burst capture:

1. The heartbeat response includes `screenshotBurstCount` and `screenshotBurstIntervalSeconds`.
2. The player captures the specified number of screenshots at the specified interval.
3. Screenshots are written to the SMB share and notified to the server.

This triggers immediately on the server detecting a schedule change — no need to wait for the player to report a new filename.
