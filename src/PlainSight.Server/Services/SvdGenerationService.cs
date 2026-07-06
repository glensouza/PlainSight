using System.Net.WebSockets;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlainSight.Server.Data;
using PlainSight.Shared;

namespace PlainSight.Server.Services;

public sealed class SvdGenerationService(
    IHttpClientFactory httpClientFactory,
    IOptions<SvdOptions> options,
    IDbContextFactory<PlainSightDbContext> dbFactory,
    MediaMetadataService mediaMetadataService,
    VideoProcessorService videoProcessor,
    MediaFileStager stager,
    IConfiguration configuration,
    ILogger<SvdGenerationService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task GenerateAsync(SvdGenerationJob job, CancellationToken ct = default)
    {
        SvdOptions opts = options.Value;
        if (!opts.IsConfigured)
        {
            throw new InvalidOperationException("ComfyUI SVD is not configured (missing Svd:ComfyUiBaseUrl).");
        }

        Uri baseUri = new(opts.ComfyUiBaseUrl!.TrimEnd('/') + "/");
        string contentPath = MediaPathResolver.Resolve(configuration["ContentPath"] ?? "/mnt/plainsight/content");
        string sourcePath = Path.Combine(contentPath, job.SourceFileName);

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Source image not found: {sourcePath}");
        }

        // Per-job timeout so a hung ComfyUI doesn't block the worker indefinitely.
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(60, opts.RequestTimeoutSeconds)));
        CancellationToken token = timeoutCts.Token;

        using HttpClient http = httpClientFactory.CreateClient();
        http.BaseAddress = baseUri;

        string uploadedName = await this.UploadImageAsync(http, sourcePath, token);
        logger.LogInformation("SVD job {Id}: uploaded source image as {UploadedName}", job.Id, uploadedName);

        string clientId = Guid.NewGuid().ToString("N");
        ClientWebSocket? socket = await this.OpenWebSocketAsync(baseUri, clientId, token);
        try
        {
            object workflow = this.BuildSvdWorkflow(uploadedName, opts, job.MotionBucketId);
            string promptId = await this.QueuePromptAsync(http, workflow, clientId, token)
                ?? throw new InvalidOperationException("ComfyUI did not return a prompt ID.");

            logger.LogInformation("SVD job {Id}: queued as prompt {PromptId}", job.Id, promptId);

            bool finished = socket is not null && await this.WaitForExecutionAsync(socket, promptId, token);

            ComfyVideoRef? videoRef = finished
                ? await this.GetFirstOutputVideoAsync(http, promptId, token)
                : await this.PollHistoryForVideoAsync(http, promptId, token);

            if (videoRef is null)
            {
                throw new InvalidOperationException("No video output found in ComfyUI history.");
            }

            // Hold the content-sync lock across the file publish + DB insert so the sync worker
            // cannot scan the freshly published file and insert its own row first (see WatermarkVideoWorkerService).
            await ContentSyncService.SyncLock.WaitAsync(token);
            string outputFileName;
            try
            {
                outputFileName = await this.DownloadAndSaveAsync(http, videoRef, contentPath, opts.FilenamePrefix, token);
                logger.LogInformation("SVD job {Id}: saved as {OutputFileName}", job.Id, outputFileName);

                await this.SaveContentItemAsync(job, outputFileName, contentPath, token);
            }
            finally
            {
                ContentSyncService.SyncLock.Release();
            }

            job.OutputFileName = outputFileName;
        }
        finally
        {
            socket?.Dispose();
        }
    }

    private async Task<string> UploadImageAsync(HttpClient http, string sourcePath, CancellationToken ct)
    {
        await using FileStream fs = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        using MultipartFormDataContent form = new();
        // StreamContent is owned by form — form.Dispose() disposes it; no separate using needed.
        StreamContent imageContent = new(fs);
        string mimeType = Path.GetExtension(sourcePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "image/png"
        };
        imageContent.Headers.ContentType = new(mimeType);
        form.Add(imageContent, "image", Path.GetFileName(sourcePath));
        form.Add(new StringContent("input"), "type");
        form.Add(new StringContent("true"), "overwrite");

        using HttpResponseMessage response = await http.PostAsync("upload/image", form, ct);
        response.EnsureSuccessStatusCode();
        UploadImageResponse? result = await response.Content.ReadFromJsonAsync<UploadImageResponse>(JsonOptions, ct);
        return result?.Name ?? throw new InvalidOperationException("ComfyUI upload/image returned no filename.");
    }

    private object BuildSvdWorkflow(string uploadedImageName, SvdOptions opts, int motionBucketId)
    {
        int seed = Random.Shared.Next(int.MaxValue);
        return new Dictionary<string, object>
        {
            ["1"] = new
            {
                class_type = "ImageOnlyCheckpointLoader",
                inputs = new { ckpt_name = opts.Model }
            },
            ["2"] = new
            {
                class_type = "LoadImage",
                inputs = new { image = uploadedImageName }
            },
            ["3"] = new
            {
                class_type = "ImageScale",
                inputs = new
                {
                    image = new object[] { "2", 0 },
                    width = opts.OutputWidth,
                    height = opts.OutputHeight,
                    upscale_method = "lanczos",
                    crop = "center"
                }
            },
            ["4"] = new
            {
                class_type = "SVD_img2vid_Conditioning",
                inputs = new
                {
                    clip_vision = new object[] { "1", 1 },
                    init_image = new object[] { "3", 0 },
                    vae = new object[] { "1", 2 },
                    width = opts.OutputWidth,
                    height = opts.OutputHeight,
                    video_frames = opts.VideoFrames,
                    motion_bucket_id = motionBucketId,
                    fps = opts.Fps,
                    augmentation_level = 0.0
                }
            },
            ["5"] = new
            {
                class_type = "VideoLinearCFGGuidance",
                inputs = new
                {
                    model = new object[] { "1", 0 },
                    min_cfg = 1.0
                }
            },
            ["6"] = new
            {
                class_type = "KSampler",
                inputs = new
                {
                    model = new object[] { "5", 0 },
                    positive = new object[] { "4", 0 },
                    negative = new object[] { "4", 1 },
                    latent_image = new object[] { "4", 2 },
                    seed,
                    steps = opts.Steps,
                    cfg = opts.Cfg,
                    sampler_name = "euler",
                    scheduler = "karras",
                    denoise = 1.0
                }
            },
            // VAEDecodeTiledVideo avoids OOM on large frame counts (25 × 1024×576 latents).
            ["7"] = new
            {
                class_type = "VAEDecodeTiledVideo",
                inputs = new
                {
                    samples = new object[] { "6", 0 },
                    vae = new object[] { "1", 2 },
                    tile_size = 256
                }
            },
            ["8"] = new
            {
                class_type = "VHS_VideoCombine",
                inputs = new
                {
                    images = new object[] { "7", 0 },
                    frame_rate = opts.Fps,
                    loop_count = 0,
                    filename_prefix = opts.FilenamePrefix,
                    format = "video/h264-mp4",
                    pingpong = false,
                    save_output = true
                }
            }
        };
    }

    private async Task<string?> QueuePromptAsync(HttpClient http, object workflow, string clientId, CancellationToken ct)
    {
        QueuePromptRequest payload = new() { ClientId = clientId, Prompt = workflow };
        using HttpResponseMessage response = await http.PostAsJsonAsync("prompt", payload, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("ComfyUI /prompt returned {StatusCode}", response.StatusCode);
            return null;
        }
        QueuePromptResponse? result = await response.Content.ReadFromJsonAsync<QueuePromptResponse>(JsonOptions, ct);
        return result?.PromptId;
    }

    private async Task<ClientWebSocket?> OpenWebSocketAsync(Uri baseUri, string clientId, CancellationToken ct)
    {
        string wsScheme = baseUri.Scheme == "https" ? "wss" : "ws";
        Uri wsUri = new($"{wsScheme}://{baseUri.Authority}/ws?clientId={clientId}");
        ClientWebSocket socket = new();
        try
        {
            await socket.ConnectAsync(wsUri, ct);
            return socket;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ComfyUI WebSocket connect failed; will poll history instead");
            socket.Dispose();
            return null;
        }
    }

    private async Task<bool> WaitForExecutionAsync(ClientWebSocket socket, string promptId, CancellationToken ct)
    {
        byte[] buffer = new byte[8192];
        StringBuilder current = new();
        while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ComfyUI WebSocket receive failed");
                return false;
            }
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return false;
            }
            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }
            current.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage)
            {
                continue;
            }
            if (IsExecutionFinished(current.ToString(), promptId))
            {
                return true;
            }
            current.Clear();
        }
        return false;
    }

    private static bool IsExecutionFinished(string message, string promptId)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(message);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("type", out JsonElement typeEl))
            {
                return false;
            }
            string? type = typeEl.GetString();
            if (type is not ("executing" or "execution_success"))
            {
                return false;
            }
            if (!root.TryGetProperty("data", out JsonElement data))
            {
                return false;
            }
            if (!data.TryGetProperty("prompt_id", out JsonElement pid) || pid.GetString() != promptId)
            {
                return false;
            }
            if (type == "execution_success")
            {
                return true;
            }
            // "executing" with node: null signals end-of-queue for this prompt
            return data.TryGetProperty("node", out JsonElement node) && node.ValueKind == JsonValueKind.Null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<ComfyVideoRef?> GetFirstOutputVideoAsync(HttpClient http, string promptId, CancellationToken ct)
    {
        using HttpResponseMessage response = await http.GetAsync($"history/{promptId}", ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
        using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty(promptId, out JsonElement entry))
        {
            return null;
        }
        if (!entry.TryGetProperty("outputs", out JsonElement outputs))
        {
            return null;
        }
        // VHS_VideoCombine stores results under "gifs"
        foreach (JsonProperty nodeProp in outputs.EnumerateObject())
        {
            if (!nodeProp.Value.TryGetProperty("gifs", out JsonElement gifs))
            {
                continue;
            }
            foreach (JsonElement gif in gifs.EnumerateArray())
            {
                string? filename = gif.TryGetProperty("filename", out JsonElement fn) ? fn.GetString() : null;
                if (string.IsNullOrEmpty(filename))
                {
                    continue;
                }
                string subfolder = gif.TryGetProperty("subfolder", out JsonElement sf) ? sf.GetString() ?? string.Empty : string.Empty;
                string folderType = gif.TryGetProperty("type", out JsonElement t) ? t.GetString() ?? "output" : "output";
                return new ComfyVideoRef { Filename = filename, Subfolder = subfolder, FolderType = folderType };
            }
        }
        return null;
    }

    private async Task<ComfyVideoRef?> PollHistoryForVideoAsync(HttpClient http, string promptId, CancellationToken ct)
    {
        TimeSpan delay = TimeSpan.FromSeconds(3);
        while (!ct.IsCancellationRequested)
        {
            ComfyVideoRef? video = await this.GetFirstOutputVideoAsync(http, promptId, ct);
            if (video is not null)
            {
                return video;
            }
            try
            {
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return null;
            }
        }
        return null;
    }

    private async Task<string> DownloadAndSaveAsync(HttpClient http, ComfyVideoRef video, string contentPath, string prefix, CancellationToken ct)
    {
        string query = $"view?filename={Uri.EscapeDataString(video.Filename)}&subfolder={Uri.EscapeDataString(video.Subfolder)}&type={Uri.EscapeDataString(video.FolderType)}";
        using HttpResponseMessage response = await http.GetAsync(query, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        string outputFileName = $"{prefix}_{DateTime.UtcNow:yyyyMMddHHmmssfff}.mp4";
        string outputPath = Path.Combine(contentPath, outputFileName);
        string workPath = stager.CreateWorkPath(outputPath);

        try
        {
            await using Stream src = await response.Content.ReadAsStreamAsync(ct);
            await using FileStream dest = new(workPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true);
            await src.CopyToAsync(dest, ct);
        }
        catch
        {
            if (File.Exists(workPath))
            {
                try
                {
                    File.Delete(workPath);
                }
                catch (IOException)
                {
                    /* best-effort cleanup of failed work file */
                }
            }

            throw;
        }

        await stager.CommitAsync(workPath, outputPath, ct);
        return outputFileName;
    }

    private async Task SaveContentItemAsync(SvdGenerationJob job, string outputFileName, string contentPath, CancellationToken ct)
    {
        string outputPath = Path.Combine(contentPath, outputFileName);
        long fileSize = new FileInfo(outputPath).Length;
        int duration = await mediaMetadataService.GetVideoDurationAsync(outputPath);

        await using PlainSightDbContext context = await dbFactory.CreateDbContextAsync(ct);
        string? thumbFileName = await videoProcessor.TryCreateThumbnailAsync(outputPath, contentPath, ct);

        ContentItem? source = await context.ContentItems.FindAsync([job.SourceContentItemId], ct);
        string sourceName = source?.Name ?? Path.GetFileNameWithoutExtension(job.SourceFileName);

        ContentItem item = new()
        {
            Name = $"SVD Animation ({sourceName})",
            FileName = outputFileName,
            Type = ContentType.Video,
            FileSizeBytes = fileSize,
            DurationSeconds = duration,
            UploadedAt = DateTime.UtcNow,
            SourceContentItemId = job.SourceContentItemId,
            ThumbnailFileName = thumbFileName
        };
        context.ContentItems.Add(item);
        await context.SaveChangesAsync(ct);
    }

    private sealed class UploadImageResponse
    {
        public string? Name { get; init; }
    }

    private sealed class QueuePromptRequest
    {
        [JsonPropertyName("client_id")]
        public required string ClientId { get; init; }

        [JsonPropertyName("prompt")]
        public required object Prompt { get; init; }
    }

    private sealed class QueuePromptResponse
    {
        [JsonPropertyName("prompt_id")]
        public string? PromptId { get; init; }
    }

    private sealed record ComfyVideoRef
    {
        public required string Filename { get; init; }
        public required string Subfolder { get; init; }
        public required string FolderType { get; init; }
    }
}
