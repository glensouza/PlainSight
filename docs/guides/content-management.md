# Content Management

How to upload, organize, and manage media files in the PlainSight Content Library.

## Quick Path

1. Navigate to **Content Library** from the sidebar.
2. Click **Upload File** (Video or Image tab), pick a file under 500 MB, click **Upload File**.
3. Files appear in the library table automatically. Thumbnails are generated on upload.
4. Click **Sync folder** to reconcile the SMB share with the database (auto-runs every 30 s by a background worker).

## Uploading Content

Three methods are available in the upload card:

### Upload Video or Image
- Use the file picker (accepts `video/*, image/*`). Maximum 500 MB.
- The filename is timestamped on upload (`yyyyMMddHHmmss_original.ext`) to prevent collisions.
- A thumbnail sidecar (`_thumb.jpg`) is generated automatically.
- The file is written to the SMB share and a database record is created.

### Render Website to Video
- Enter a URL and a duration (5–300 seconds).
- Click **Queue Render**. The server launches a headless browser (PuppeteerSharp), captures the page, and produces an MP4.
- Monitor progress in the **Render Queue** panel at the top.

### Download from YouTube
- Paste a YouTube URL and click **Download**.
- The video is downloaded, optionally re-encoded to a max height (default 1080p), and added to the library.
- Monitor progress in the **YouTube Download Queue** panel.

## Folder Sync

The `ContentSyncWorkerService` runs every 30 seconds and reconciles files on the SMB share with the database:
- New files on disk get database records.
- Deleted files get database records removed.
- Modified files (size/duration change) get records updated.

You can trigger a manual sync with the **Sync folder** button. The result banner shows `+N added, -N removed, ~N updated`.

## Managing Content

Each item in the library table has action buttons:

| Button | Description |
|---|---|
| **Preview** (`eye` icon) | Opens a modal with `<video controls autoplay>` for videos or `<img>` for images. |
| **Process Video** (`gear` icon) | Opens a modal with Strip Audio and Compression checkboxes. Creates a new item, does not overwrite. |
| **Edit** (`scissors` icon) | Opens the video editor modal: trim, crop, reverse, speed (0.5x–2.0x), strip audio, compress. Creates a new item. |
| **Ken Burns** (`film` icon) | Image only. Opens the Ken Burns modal for zoom-pan animation. |
| **SVD Animate** (`stars` icon) | Image only. Visible only when `Svd:ComfyUiBaseUrl` is configured. |
| **Rename** (`pencil` icon) | Edit the display name, filename, or set a companion clip to play before/after. |
| **Remove Watermark** (`eraser` icon) | Removes Veo/Gemini watermarks via ffmpeg. Creates a new item. |
| **Delete** (`trash` icon) | Admin only. Permanently deletes the file from disk and the database record. |

## Companion Content

Two content items can be paired so one always plays immediately before or after the other in any playlist. The server expands companion pairs at heartbeat time — no baked MP4 is created.

Set a companion via the Rename modal: choose a **Companion Clip** from the dropdown and select **Before** or **After**.

## Idle Content

Files in the Idle Content Library play when no schedule is active. Upload via the **Idle Library** page — idle files are stored separately from main content and play in alphabetical order.

The idle folder has its own FileSystemWatcher for live updates, and refreshes every 400 ms when changes are detected.
