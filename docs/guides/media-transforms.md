# Media Transforms

How to convert, edit, and transform media in the Content Library. Every transform creates a **new** `ContentItem` linked to the source via `SourceContentItemId` — the original is never overwritten.

## Quick Path

1. Find an image in the Content Library.
2. Click **Ken Burns** (`film` icon), adjust the start/end rectangles and duration, click **Apply Ken Burns**.
3. A new video item appears in the library, linked to the original image.

## Image-to-Video

Convert a static image into a looping MP4.

- Click **Ken Burns** on an image to open the modal if you want a zoom-pan effect. For a static loop, use the **SVD Animate** button or the API directly.
- Duration: 1–3600 seconds.
- Creates a new video item with the suffix `_video.mp4`.

## Ken Burns (Zoom-Pan Animation)

Available on any image in the library.

**Modal controls:**
- **Start Position**: Blue rectangle overlay — drag/resize to set the crop at the start of the animation.
- **End Position**: Orange rectangle overlay — crop at the end.
- **Values**: Left %, Top %, Width % (all normalized 0.0–1.0; `x + w` must be ≤ 1).
- **Duration**: 1–3600 seconds.
- **Overlay image** (optional): Layer another image on top. Select from the content dropdown.
- **Parallax rate**: Slider 0–100%. Controls how much the overlay "moves" relative to the background.

Click **Apply Ken Burns** to render. Output filename: `{original}_kenburns_{timestamp}.mp4`.

## Extract Frame

Extract a single frame from a video as a new JPEG image.

Available via API only: `POST /api/content/{id}/extract-frame?position=first` or `position=last`. Creates a new image item with suffix `_frame_first.jpg` or `_frame_last.jpg`.

## Video Editor

Click the **Edit** (`scissors`) icon on any video. The modal has:

### Trim
Two draggable handles on the timeline track. Click **Set Start** / **Set End** at the current playback position to snap handles.

### Crop
- Select an aspect ratio from the dropdown: Free, Original, 16:9, 4:3, 1:1.
- A green box overlay appears on the preview. Drag to position.
- Click **Reset crop** to clear.

### Options
- **Strip Audio**: Remove the audio track.
- **Compression**: Re-encode with smaller file size.
- **Reverse**: Play the video backwards.
- **Speed**: 0.5x, 1x, 1.5x, or 2.0x.

Modify the **Name** and **File Name** fields if desired. Click **Apply Edit** to render.

All edits are composited into a single ffmpeg command — only one re-encode pass is required.

## Process Video (Quick Strip + Compress)

A simplified version of the editor. Click **Process Video** (`gear` icon) to open a modal with only two checkboxes: Strip Audio and Compression. Click **Process and Replace**.

## Remove Watermark

Removes Veo/Gemini watermarks from videos via a single ffmpeg pass (reverse-alpha blend). Works on any content item — video or image.

Click **Remove Watermark** (`eraser` icon). The job is added to the **Watermark Removal Queue** and processes asynchronously. A new item appears when done.

## Thumbnails

Thumbnails are auto-generated at upload/sync time as `_thumb.jpg` sidecar files. They are not separate database records — they live next to the source file on the SMB share.
