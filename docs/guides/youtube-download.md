# YouTube Download

How to download videos from YouTube into the Content Library and configure re-encoding settings.

## Quick Path

1. Navigate to **Content Library**.
2. Paste a YouTube URL in the **Download from YouTube** card.
3. Click **Download**.
4. Monitor progress in the **YouTube Download Queue** at the top of the page.
5. The finished video appears in the library table.

## Limits

Two configurable limits prevent oversized downloads:

| Config Key | Default | Description |
|---|---|---|
| `YouTube:MaxDownloadBytes` | 2 GB (`2147483648`) | Rejects downloads exceeding this size. |
| `YouTube:MaxDurationSeconds` | 2 hours (`7200`) | Rejects videos longer than this. |

If a video exceeds either limit, the download is rejected with a status message in the queue panel.

## Video Shrink (Re-encoding)

After download, videos are optionally re-encoded via ffmpeg. This reduces file size and normalizes the codec for smooth playback on Raspberry Pi players.

### Enable/Disable
- `YouTube:Shrink:Enabled` (default `true`). Set to `false` to skip re-encoding and use the raw download.

### Shrink Settings

| Config Key | Default | Description |
|---|---|---|
| `YouTube:Shrink:MaxHeight` | `1080` | Scale down if the source height exceeds this. |
| `YouTube:Shrink:Crf` | `28` | Constant Rate Factor. Higher = smaller file, lower quality. |
| `YouTube:Shrink:Preset` | `medium` | ffmpeg encoding preset (`fast`, `medium`, `slow`, etc.). |
| `YouTube:Shrink:AudioBitrate` | `128k` | Audio bitrate for the output (`-b:a`). |

### What Happens

1. The download worker fetches the video using `yt-dlp` (must be installed on the server).
2. If shrink is enabled, the raw download is passed through ffmpeg with the configured settings.
3. The output is written to `ContentPath` on the SMB share.
4. A database record is created, and `ContentSyncService` picks it up on the next sync cycle.

## Queue Monitoring

The **YouTube Download Queue** panel appears at the top of the Content Library page when jobs are in progress. It shows:

- URL being downloaded.
- Status: Queued / Processing / Done / Failed.
- Dismiss button (X) to clear completed/failed jobs.

The queue auto-polls every few seconds and refreshes the content library when jobs complete.

## Troubleshooting

- **"Download failed"**: Check that `yt-dlp` is installed on the server and the URL is valid.
- **"Video too large" / "Video too long"**: Adjust `YouTube:MaxDownloadBytes` or `YouTube:MaxDurationSeconds`.
- **File not appearing**: The `ContentSyncService` runs every 30 seconds. Click **Sync folder** to force an immediate scan.
- **Playback issues on Pi**: Ensure `YouTube:Shrink:Enabled` is `true` — the raw YouTube codec may not be hardware-decodable on the Raspberry Pi.
