# AI Media Workflow

End-to-end flow for generating animated video from static images using AI, then preparing the output for signage playlists.

## Gemini/Veo Pipeline (Quick Path)

1. Use Google's Gemini/Veo to animate a static image — prompt engineering matters. The prompts library in this repo provides battle-tested templates.
2. Download the generated video and **Upload** it to the Content Library (Video or Image tab).
3. The Veo output includes a watermark. Click **Remove Watermark** (`eraser` icon) on the new item.
4. Monitor the **Watermark Removal Queue** — the clean file appears as a new item in the library.
5. (Optional) Create an **Announcement** grouping the original static image and the watermark-free video, image first. This creates a slide→video sequence anywhere the announcement is added to a playlist.
6. Add to a playlist and schedule.

## SVD (Stable Video Diffusion) Self-Hosted Alternative

SVD generates motion from a static image using ComfyUI running on your own hardware.

### Prerequisites

- A ComfyUI server with the SVD checkpoint (`svd_xt.safetensors`) installed.
- The `Svd:ComfyUiBaseUrl` config key set to the ComfyUI server URL (e.g. `http://comfyui:8188`).

**Hardware note**: M4 Pro Mac was used for testing (closed issue #86). GPU acceleration is recommended — SVD is compute-intensive.

### Configuration

All SVD settings live under the `Svd:` config section. See [Configuration Reference](../configuration.md#svd-stable-video-diffusion) for the full table. Key knobs:

| Key | Default | Effect |
|---|---|---|
| `Svd:ComfyUiBaseUrl` | `null` | Must be set — SVD is disabled otherwise. |
| `Svd:MotionBucketId` | `127` | Motion amount (1–255). Higher = more motion. |
| `Svd:VideoFrames` | `25` | Output frame count. |
| `Svd:Fps` | `6` | Frames per second. A 25-frame clip at 6 fps is ~4 seconds. |
| `Svd:OutputWidth` / `Svd:OutputHeight` | `1024` / `576` | Output dimensions. |

### Generating an SVD Animation

1. Find an image in the Content Library.
2. Click **SVD Animate** (`stars` icon) — only visible if ComfyUI is configured.
3. Adjust the **Motion Amount** slider (1–255).
4. Click **Queue Animation**. The job appears in the **SVD Animation Queue** (green spinner).
5. When complete, a new video item appears in the library.

### Post-SVD Workflow

Same as Veo: upload → remove watermark → group into an announcement → playlist.

## Watermark Removal

Both Gemini/Veo and some SVD outputs include visible watermarks. The **Remove Watermark** action uses a single-pass ffmpeg reverse-alpha blend to strip them.

- Works on video and image items.
- The job runs in the **Watermark Removal Queue** (queue-driven background worker).
- Output is a new `ContentItem` with `SourceContentItemId` pointing to the original.
- The original is never modified.

## Announcements (Image + Video Sequences)

After watermark removal, the original static image and the clean video can be grouped into an **Announcement** so they always play back-to-back, in order, wherever the announcement is added to a playlist:

1. Navigate to **Announcements** and create a new one (title, optional event date/expiration).
2. **Manage Media** → add the original image, then the watermark-free video.
3. Add the announcement to a playlist from the playlist's **Add Announcement** list — the server expands its media in order at heartbeat/preview time (no baked MP4).
