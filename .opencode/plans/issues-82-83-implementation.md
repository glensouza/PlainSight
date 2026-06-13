# Issues #82 + #83 — Full Implementation Plan

## Overview

Two feature branches, two draft PRs. Issue #82 first (no dependencies), then #83.

| Issue | Branch | Title |
|---|---|---|
| #82 | `issue-82-media-transforms` | Media transform primitives (ffmpeg): image→video, video→frame, thumbnails |
| #83 | `issue-83-watermark-removal` | Gemini watermark removal via reverse alpha blending + ffmpeg delogo |

---

## Issue #82 — Media Transform Primitives

### Files to create/modify

| File | Action |
|---|---|
| `src/PlainSight.Server/Models/ContentItem.cs` | Add 2 properties |
| `src/PlainSight.Server/Services/MediaMetadataService.cs` | Add `GetVideoDimensionsAsync` |
| `src/PlainSight.Server/Services/VideoProcessorService.cs` | Add 3 transform methods |
| `src/PlainSight.Server/Api/TransformApi.cs` | New file — 2 endpoints |
| `src/PlainSight.Server/Components/Pages/Content.razor` | Auto-thumbnail + transform buttons |
| `src/PlainSight.Server/Program.cs` | `app.MapTransformApi()` |
| EF Migration | `dotnet ef migrations add AddSourceContentItemIdAndThumbnailFileName` |

---

### 1. ContentItem model changes

**File:** `src/PlainSight.Server/Models/ContentItem.cs`

Add two properties:

```csharp
public int? SourceContentItemId { get; set; }
public string? ThumbnailFileName { get; set; }
```

Full file becomes:

```csharp
namespace PlainSight.Server.Models;

public class ContentItem
{
    public int Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public ContentType Type { get; set; }
    public long FileSizeBytes { get; set; }
    public int DurationSeconds { get; set; }
    public DateTime UploadedAt { get; init; }
    public string? Description { get; set; }
    public string? SourceUrl { get; set; }
    public int? SourceContentItemId { get; set; }
    public string? ThumbnailFileName { get; set; }
}

public enum ContentType
{
    Video,
    Image,
    RenderedWebsite
}
```

### 2. EF Migration

```bash
dotnet ef migrations add AddSourceContentItemIdAndThumbnailFileName --project src/PlainSight.Server
```

This auto-generates the migration adding nullable `SourceContentItemId` (FK to ContentItems.Id) and `ThumbnailFileName`.

---

### 3. MediaMetadataService — new method

**File:** `src/PlainSight.Server/Services/MediaMetadataService.cs`

Add method (uses same ffprobe pattern as `GetVideoDurationAsync`):

```csharp
public async Task<(int width, int height)> GetVideoDimensionsAsync(string filePath)
{
    if (!File.Exists(filePath))
    {
        logger.LogWarning("File not found for dimension extraction: {FilePath}", filePath);
        return (1920, 1080);
    }

    try
    {
        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=s=x:p=0 \"{filePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();
        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        string[] parts = output.Trim().Split('x');
        if (parts.Length == 2 && int.TryParse(parts[0], out int width) && int.TryParse(parts[1], out int height))
        {
            return (width, height);
        }

        logger.LogWarning("Failed to parse video dimensions: {Output}", output);
        return (1920, 1080);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error extracting video dimensions for {FilePath}", filePath);
        return (1920, 1080);
    }
}
```

---

### 4. VideoProcessorService — 3 new methods

**File:** `src/PlainSight.Server/Services/VideoProcessorService.cs`

Add these three methods in the class (before closing brace):

```csharp
public async Task ConvertImageToVideoAsync(string inputPath, string outputPath, int durationSeconds, CancellationToken cancellationToken = default)
{
    this.logger.LogInformation("Converting image to video: {InputPath} -> {OutputPath} ({Duration}s)", inputPath, outputPath, durationSeconds);

    // Get image dimensions to set output size correctly
    string extension = Path.GetExtension(inputPath).ToLowerInvariant();
    StringBuilder arguments = new();
    arguments.Append($"-y -loop 1 -i \"{inputPath}\" -t {durationSeconds.ToString(CultureInfo.InvariantCulture)} ");
    arguments.Append("-c:v libx264 -pix_fmt yuv420p -preset fast -crf 23 ");
    arguments.Append($"-movflags +faststart -f mp4 \"{outputPath}\"");

    await this.RunFfmpegAsync(arguments.ToString(), cancellationToken);
    this.logger.LogInformation("Image-to-video conversion complete: {OutputPath}", outputPath);
}

public async Task ExtractFirstFrameAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)
{
    this.logger.LogInformation("Extracting first frame: {InputPath} -> {OutputPath}", inputPath, outputPath);

    StringBuilder arguments = new();
    arguments.Append($"-y -i \"{inputPath}\" -vframes 1 ");
    arguments.Append($"\"{outputPath}\"");

    await this.RunFfmpegAsync(arguments.ToString(), cancellationToken);
    this.logger.LogInformation("First frame extraction complete: {OutputPath}", outputPath);
}

public async Task ExtractLastFrameAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)
{
    this.logger.LogInformation("Extracting last frame: {InputPath} -> {OutputPath}", inputPath, outputPath);

    // Use ffprobe to get duration, seek to near-end, grab last frame
    using Process probe = new()
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{inputPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };

    probe.Start();
    string durationStr = await probe.StandardOutput.ReadToEndAsync(cancellationToken);
    await probe.WaitForExitAsync(cancellationToken);

    double duration = double.TryParse(durationStr.Trim(), CultureInfo.InvariantCulture, out double d) ? d : 10.0;
    double seekTime = Math.Max(0, duration - 0.5);

    StringBuilder arguments = new();
    arguments.Append(CultureInfo.InvariantCulture, $"-y -ss {seekTime:F3} -i \"{inputPath}\" -vframes 1 ");
    arguments.Append($"\"{outputPath}\"");

    await this.RunFfmpegAsync(arguments.ToString(), cancellationToken);
    this.logger.LogInformation("Last frame extraction complete: {OutputPath}", outputPath);
}

private async Task RunFfmpegAsync(string args, CancellationToken ct)
{
    using Process process = new()
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = args,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };

    StringBuilder ffmpegError = new();
    process.ErrorDataReceived += (_, e) =>
    {
        if (e.Data != null)
        {
            ffmpegError.AppendLine(e.Data);
            this.logger.LogDebug("FFmpeg: {Log}", e.Data);
        }
    };

    if (!process.Start())
    {
        throw new InvalidOperationException("Failed to start FFmpeg process");
    }

    process.BeginErrorReadLine();

    try
    {
        await process.WaitForExitAsync(ct);
    }
    catch (OperationCanceledException)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            /* process never started or already exited */
        }

        throw;
    }

    if (process.ExitCode != 0)
    {
        string errorDetails = ffmpegError.ToString();
        this.logger.LogError("FFmpeg failed with exit code {ExitCode}. Output: {Output}", process.ExitCode, errorDetails);
        throw new InvalidOperationException($"FFmpeg exited with code {process.ExitCode}. Details: {errorDetails}");
    }
}
```

**Note:** The existing `ProcessVideoAsync` method should be refactored to use the shared `RunFfmpegAsync` helper to avoid duplication. Replace the inline ffmpeg Process logic in `ProcessVideoAsync` with a call to `this.RunFfmpegAsync(arguments.ToString(), cancellationToken)`. The existing method body from line 51-106 gets replaced.

---

### 5. TransformApi — new REST endpoints

**File:** `src/PlainSight.Server/Api/TransformApi.cs` (create new)

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlainSight.Server.Data;
using PlainSight.Server.Models;
using PlainSight.Server.Services;
using PlainSight.Shared;

namespace PlainSight.Server.Api;

public static class TransformApi
{
    public static void MapTransformApi(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/content")
            .WithGroupName("Content Transforms")
            .RequireAuthorization();

        group.MapPost("/{id:int}/convert-to-video", async (
            int id,
            int? durationSeconds,
            IDbContextFactory<PlainSightDbContext> dbFactory,
            VideoProcessorService videoProcessor,
            MediaMetadataService mediaMetadataService,
            IConfiguration configuration,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            ILogger logger = loggerFactory.CreateLogger("TransformApi");
            string contentPath = MediaPathResolver.Resolve(configuration["ContentPath"] ?? "/mnt/plainsight/content");
            int dur = durationSeconds.GetValueOrDefault(10);

            await using PlainSightDbContext dbContext = await dbFactory.CreateDbContextAsync(ct);
            ContentItem? item = await dbContext.ContentItems.FindAsync([id], ct);
            if (item == null || item.Type != ContentType.Image)
            {
                return Results.BadRequest("Item not found or not an image");
            }

            string inputPath = Path.Combine(contentPath, item.FileName);
            if (!File.Exists(inputPath))
            {
                return Results.NotFound();
            }

            string extension = Path.GetExtension(item.FileName);
            string baseName = Path.GetFileNameWithoutExtension(item.FileName);
            string outputFileName = $"{baseName}_video.mp4";
            string outputPath = Path.Combine(contentPath, outputFileName);

            try
            {
                await videoProcessor.ConvertImageToVideoAsync(inputPath, outputPath, dur, ct);

                int videoDuration = await mediaMetadataService.GetVideoDurationAsync(outputPath);
                long fileSize = new FileInfo(outputPath).Length;

                ContentItem newItem = new()
                {
                    Name = $"{item.Name} (video)",
                    FileName = outputFileName,
                    Type = ContentType.Video,
                    FileSizeBytes = fileSize,
                    DurationSeconds = videoDuration,
                    UploadedAt = DateTime.UtcNow,
                    Description = item.Description,
                    SourceContentItemId = item.Id
                };

                dbContext.ContentItems.Add(newItem);
                await dbContext.SaveChangesAsync(ct);

                logger.LogInformation("Image {Id} converted to video {NewId}", id, newItem.Id);
                return Results.Ok(new { id = newItem.Id, fileName = outputFileName });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to convert image {Id} to video", id);
                return Results.Problem($"Conversion failed: {ex.Message}", statusCode: 500);
            }
        });

        group.MapPost("/{id:int}/extract-frame/{position}", async (
            int id,
            string position,
            IDbContextFactory<PlainSightDbContext> dbFactory,
            VideoProcessorService videoProcessor,
            IConfiguration configuration,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            ILogger logger = loggerFactory.CreateLogger("TransformApi");
            string contentPath = MediaPathResolver.Resolve(configuration["ContentPath"] ?? "/mnt/plainsight/content");

            await using PlainSightDbContext dbContext = await dbFactory.CreateDbContextAsync(ct);
            ContentItem? item = await dbContext.ContentItems.FindAsync([id], ct);
            if (item == null || item.Type != ContentType.Video)
            {
                return Results.BadRequest("Item not found or not a video");
            }

            string inputPath = Path.Combine(contentPath, item.FileName);
            if (!File.Exists(inputPath))
            {
                return Results.NotFound();
            }

            string baseName = Path.GetFileNameWithoutExtension(item.FileName);
            string suffix = position.ToLowerInvariant() switch { "last" => "_lastframe", _ => "_firstframe" };
            string outputFileName = $"{baseName}{suffix}.png";
            string outputPath = Path.Combine(contentPath, outputFileName);

            try
            {
                if (position.Equals("last", StringComparison.OrdinalIgnoreCase))
                {
                    await videoProcessor.ExtractLastFrameAsync(inputPath, outputPath, ct);
                }
                else
                {
                    await videoProcessor.ExtractFirstFrameAsync(inputPath, outputPath, ct);
                }

                long fileSize = new FileInfo(outputPath).Length;

                ContentItem newItem = new()
                {
                    Name = $"{item.Name} ({position} frame)",
                    FileName = outputFileName,
                    Type = ContentType.Image,
                    FileSizeBytes = fileSize,
                    DurationSeconds = 10,
                    UploadedAt = DateTime.UtcNow,
                    SourceContentItemId = item.Id
                };

                dbContext.ContentItems.Add(newItem);
                await dbContext.SaveChangesAsync(ct);

                logger.LogInformation("Frame extracted from video {Id}: {Position} -> {NewId}", id, position, newItem.Id);
                return Results.Ok(new { id = newItem.Id, fileName = outputFileName });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to extract frame from video {Id}", id);
                return Results.Problem($"Frame extraction failed: {ex.Message}", statusCode: 500);
            }
        });
    }
}
```

---

### 6. Content.razor changes

**File:** `src/PlainSight.Server/Components/Pages/Content.razor`

#### 6a. Add HttpClient inject (line 8 area)
Add after `@inject ILogger<Content> Logger`:
```razor
@inject IHttpClientFactory HttpClientFactory
```

#### 6b. Add new state variables (in @code block, near other state vars)
Add after `private ContentItem? itemToDelete;`:
```csharp
private ContentItem? transformingItem;
private bool isTransforming;
private int transformDuration = 10;
private ContentItem? dewatermarkingItem;
private bool isDewatermarking;
```

#### 6c. Add transform buttons in actions column (inside btn-group)

After the Process Video button block (`@if (item.Type != ContentType.Image) { ... }`) add:

```razor
@if (item.Type == ContentType.Image)
{
    <button class="btn btn-outline-success" @onclick="() => StartImageToVideo(item)" title="Convert to Video">
        <i class="bi bi-camera-video"></i>
    </button>
}
@if (item.Type == ContentType.Video)
{
    <div class="btn-group btn-group-sm">
        <button class="btn btn-outline-success dropdown-toggle" data-bs-toggle="dropdown" title="Extract Frame">
            <i class="bi bi-camera"></i>
        </button>
        <ul class="dropdown-menu">
            <li><button class="dropdown-item" @onclick="() => ExtractFrame(item, &quot;first&quot;)">First Frame</button></li>
            <li><button class="dropdown-item" @onclick="() => ExtractFrame(item, &quot;last&quot;)">Last Frame</button></li>
        </ul>
    </div>
}
```

**Note:** The dropdown pattern may need Blazor's built-in Bootstrap dropdown instead of `data-bs-toggle`. Use a simple approach:

```razor
@if (item.Type == ContentType.Video)
{
    <button class="btn btn-outline-success" @onclick="() => ExtractFrame(item, &quot;first&quot;)" title="Extract First Frame">
        <i class="bi bi-camera"></i>
    </button>
    <button class="btn btn-outline-success" @onclick="() => ExtractFrame(item, &quot;last&quot;)" title="Extract Last Frame">
        <i class="bi bi-camera-reverse"></i>
    </button>
}
```

#### 6d. Add watermark removal button (ISSUE #83)

After the transform buttons, before the Rename button, add:

```razor
<button class="btn btn-outline-warning" @onclick="() => RemoveWatermark(item)" title="Remove Watermark">
    <i class="bi bi-droplet"></i>
</button>
```

#### 6e. Add handler methods (in @code block, before Dispose)

```csharp
private void StartImageToVideo(ContentItem item)
{
    this.transformingItem = item;
    this.transformDuration = 10;
}

private async Task PerformImageToVideo()
{
    if (this.transformingItem == null)
    {
        return;
    }

    this.isTransforming = true;
    try
    {
        HttpClient client = this.HttpClientFactory.CreateClient();
        string url = $"/api/content/{this.transformingItem.Id}/convert-to-video?durationSeconds={this.transformDuration}";
        HttpResponseMessage response = await client.PostAsync(url, null, this.cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync();
            this.uploadError = $"Conversion failed: {error}";
            return;
        }

        this.transformingItem = null;
        await this.LoadContent();
    }
    catch (Exception ex)
    {
        this.Logger.LogError(ex, "Error converting image to video");
        this.uploadError = $"Conversion failed: {ex.Message}";
    }
    finally
    {
        this.isTransforming = false;
    }
}

private async Task ExtractFrame(ContentItem item, string position)
{
    this.isTransforming = true;
    try
    {
        HttpClient client = this.HttpClientFactory.CreateClient();
        string url = $"/api/content/{item.Id}/extract-frame/{position}";
        HttpResponseMessage response = await client.PostAsync(url, null, this.cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync();
            this.uploadError = $"Frame extraction failed: {error}";
            return;
        }

        await this.LoadContent();
    }
    catch (Exception ex)
    {
        this.Logger.LogError(ex, "Error extracting frame from video");
        this.uploadError = $"Frame extraction failed: {ex.Message}";
    }
    finally
    {
        this.isTransforming = false;
    }
}

private async Task RemoveWatermark(ContentItem item)
{
    this.isDewatermarking = true;
    try
    {
        HttpClient client = this.HttpClientFactory.CreateClient();
        string url = $"/api/content/{item.Id}/remove-watermark";
        HttpResponseMessage response = await client.PostAsync(url, null, this.cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync();
            this.uploadError = $"Watermark removal failed: {error}";
            return;
        }

        await this.LoadContent();
    }
    catch (Exception ex)
    {
        this.Logger.LogError(ex, "Error removing watermark");
        this.uploadError = $"Watermark removal failed: {ex.Message}";
    }
    finally
    {
        this.isDewatermarking = false;
    }
}

private async Task GenerateThumbnail(ContentItem item, string filePath)
{
    try
    {
        string contentPath = MediaPathResolver.Resolve(this.Configuration["ContentPath"] ?? "/mnt/plainsight/content");
        string baseName = Path.GetFileNameWithoutExtension(item.FileName);
        string thumbFileName = $"{baseName}_thumb.png";
        string thumbPath = Path.Combine(contentPath, thumbFileName);

        if (item.Type == ContentType.Image)
        {
            // Copy and resize image as thumbnail
            using SixLabors.ImageSharp.Image image = SixLabors.ImageSharp.Image.Load(filePath);
            image.Mutate(x => x.Resize(320, 0));
            image.SaveAsPng(thumbPath);
        }
        else if (item.Type == ContentType.Video)
        {
            // Extract first frame as thumbnail
            await this.VideoProcessorService.ExtractFirstFrameAsync(filePath, thumbPath, this.cts.Token);
            // Resize the thumbnail
            if (File.Exists(thumbPath))
            {
                using SixLabors.ImageSharp.Image image = SixLabors.ImageSharp.Image.Load(thumbPath);
                image.Mutate(x => x.Resize(320, 0));
                image.SaveAsPng(thumbPath);
            }
        }

        if (File.Exists(thumbPath))
        {
            item.ThumbnailFileName = thumbFileName;
            this.Logger.LogInformation("Thumbnail generated for {FileName}: {ThumbFileName}", item.FileName, thumbFileName);
        }
    }
    catch (Exception ex)
    {
        this.Logger.LogWarning(ex, "Failed to generate thumbnail for {FileName}", item.FileName);
    }
}
```

#### 6f. Add ImageSharp using (already in _Imports via Services namespace, but add explicit at top):
```razor
@using SixLabors.ImageSharp
@using SixLabors.ImageSharp.Processing
```

Actually, since SixLabors.ImageSharp is not in the standard usings, add it to `_Imports.razor`.

**File:** `src/PlainSight.Server/Components/_Imports.razor` — add line:
```razor
@using SixLabors.ImageSharp
@using SixLabors.ImageSharp.Processing
```

#### 6g. Call GenerateThumbnail in UploadFile method

In the `UploadFile` method, after saving the file and before `await context.SaveChangesAsync()`, add:

```csharp
await this.GenerateThumbnail(contentItem, filePath);
```

#### 6h. Modal for image-to-video duration input

Add modal (similar to processing modal pattern). After the processingItem modal block (around line 405), add:

```razor
@if (this.transformingItem != null)
{
    <div class="modal fade show d-block" tabindex="-1" style="background: rgba(0,0,0,0.5);">
        <div class="modal-dialog">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title">Convert to Video: @this.transformingItem.Name</h5>
                    <button type="button" class="btn-close" @onclick="() => this.transformingItem = null"></button>
                </div>
                <div class="modal-body">
                    <div class="mb-3">
                        <label class="form-label">Duration (seconds)</label>
                        <input type="number" class="form-control" @bind="this.transformDuration" min="1" max="300" />
                    </div>
                    @if (this.isTransforming)
                    {
                        <div class="alert alert-info py-2">
                            <span class="spinner-border spinner-border-sm me-2" role="status"></span>
                            Converting image to video...
                        </div>
                    }
                </div>
                <div class="modal-footer">
                    <button type="button" class="btn btn-secondary" @onclick="() => this.transformingItem = null" disabled="@this.isTransforming">Cancel</button>
                    <button type="button" class="btn btn-primary" @onclick="this.PerformImageToVideo" disabled="@this.isTransforming">
                        Convert
                    </button>
                </div>
            </div>
        </div>
    </div>
}
```

#### 6i. Add dewatermarking indicator

No modal needed for watermark removal — it's fire-and-forget. Add a status line near the sync message:

```razor
@if (this.isDewatermarking)
{
    <div class="alert alert-info alert-dismissible py-2 mt-2 mb-0" role="alert">
        <span class="spinner-border spinner-border-sm me-2" role="status"></span> Removing watermark...
    </div>
}
```

---

### 7. Program.cs changes

**File:** `src/PlainSight.Server/Program.cs`

Two additions:

#### 7a. Register WatermarkRemovalService (issue #83)
After line 97 (`builder.Services.AddSingleton<ScheduleChangeTracker>();`) add:
```csharp
builder.Services.AddSingleton<WatermarkRemovalService>();
```

#### 7b. Map Transform and Watermark APIs
After line 212 (`app.MapUpdateApi();`) add:
```csharp
app.MapTransformApi();
app.MapWatermarkApi();
```

---

## Issue #83 — Watermark Removal

### Files already created

- `src/PlainSight.Server/Services/WatermarkRemovalService.cs` — ✅ created
- `src/PlainSight.Server/Api/WatermarkApi.cs` — ✅ created

### Files still needing edits

| File | Action |
|---|---|
| `src/PlainSight.Server/Program.cs` | DI + `MapWatermarkApi()` (see section 7 above) |
| `src/PlainSight.Server/Components/Pages/Content.razor` | Button + handler (see sections 6d, 6i above) |

### WatermarkRemovalService algorithm

**Images (PNG/JPG/GIF/BMP/WEBP):**
- Load with `Image<Rgba32>` (SixLabors.ImageSharp)
- Process pixels in ROI: `x=0.82W, y=0.75H, w=0.15W, h=0.15H`
- Reverse alpha blending formula:
  ```
  new.R = clamp((orig.R - 255 * 0.18) / (1 - 0.18), 0, 255)
  ```
  (same for G, B)

**Videos (MP4/WEBM/MKV/AVI/MOV/M4V/TS):**
- Get dimensions via ffprobe
- Calculate delogo ROI from dimensions
- `ffmpeg -i input -vf "delogo=x={x}:y={y}:w={w}:h={h}:show=0" -c:a copy -movflags +faststart output`

### WatermarkApi endpoint

`POST /api/content/{id:int}/remove-watermark`
- Finds ContentItem, resolves file path
- Creates `{base}_dewatermarked{ext}` output filename
- Calls `WatermarkRemovalService.RemoveWatermarkAsync`
- Creates new ContentItem with `(de-watermarked)` suffix
- Returns `{ id, fileName }`

---

## NuGet Dependencies

Already added: `SixLabors.ImageSharp` v4.0.0 (`dotnet add package SixLabors.ImageSharp`)

---

## Execution Order

```bash
# 1. Create branch for issue #82
git checkout -b issue-82-media-transforms

# 2. Implement all issue #82 changes:
#    - ContentItem.cs (add 2 properties)
#    - MediaMetadataService.cs (add GetVideoDimensionsAsync)
#    - VideoProcessorService.cs (add 3 methods + refactor to shared RunFfmpegAsync)
#    - TransformApi.cs (create new)
#    - Content.razor (auto-thumbnail, transform buttons, handlers, modal)
#    - _Imports.razor (add SixLabors usings)
#    - Program.cs (MapTransformApi)

# 3. Create EF migration
dotnet ef migrations add AddSourceContentItemIdAndThumbnailFileName --project src/PlainSight.Server

# 4. Build and verify
dotnet build

# 5. Commit, push, create draft PR
git add -A
git commit -m "Add media transform primitives: image→video, video→frame, thumbnails (#82)"
git push -u origin issue-82-media-transforms
gh pr create --draft --title "Media transform primitives (ffmpeg): image→video, video→frame, thumbnails" --body "Closes #82" --base main

# 6. Create branch for issue #83
git checkout main
git checkout -b issue-83-watermark-removal

# 7. Implement all issue #83 remaining changes:
#    - Program.cs (DI + MapWatermarkApi)
#    - Content.razor (button + handler)

# 8. Build and verify
dotnet build

# 9. Commit, push, create draft PR
git add -A
git commit -m "Add Gemini/Veo watermark removal via reverse alpha blending (#83)"
git push -u origin issue-83-watermark-removal
gh pr create --draft --title "Gemini/Veo watermark removal (reverse alpha blending + ffmpeg delogo)" --body "Closes #83" --base main
```

---

## Verification Checklist

### Issue #82
- [ ] `dotnet build` succeeds
- [ ] Image file → "Convert to Video" button appears → modal with duration → creates new video ContentItem
- [ ] Video file → "Extract First Frame" / "Extract Last Frame" buttons appear → creates new image ContentItem
- [ ] Uploading new file auto-generates thumbnail (ThumbnailFileName set)
- [ ] `SourceContentItemId` links transformed items to source
- [ ] Migration created with correct columns

### Issue #83
- [ ] `dotnet build` succeeds
- [ ] Content table shows "Remove Watermark" button for each item
- [ ] Image watermark removal produces de-watermarked copy via reverse alpha blending
- [ ] Video watermark removal produces de-watermarked copy via ffmpeg delogo
- [ ] New ContentItem created with `(de-watermarked)` suffix
- [ ] API returns 200 with `{ id, fileName }`
