using SkiaSharp;

namespace PlainSight.Player.Services;

public class SplashGeneratorService(
    IConfiguration configuration,
    ILogger<SplashGeneratorService> logger) : IHostedService
{
    private const int Width = 1920;
    private const int Height = 1080;
    private const string SplashVersionFile = "splash.version";
    private const int LogoSize = 220;
    private const int Padding = 32;

    private static readonly SKColor BackgroundColor = new(10, 10, 20);
    private static readonly SKColor CyanAccent = new(34, 211, 238);
    private static readonly SKColor BlueAccent = new(59, 130, 246);
    private static readonly SKColor SubtleText = new(140, 140, 165);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Splash is only meaningful on the Pi (labwc/swaybg). Skip on Windows/macOS
        // dev runs so we don't write to a Linux-style path on the dev machine.
        if (!OperatingSystem.IsLinux())
        {
            logger.LogInformation("Skipping splash generation on non-Linux platform");
            return Task.CompletedTask;
        }

        this.GenerateSplash();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void GenerateSplash()
    {
        string tempPath = string.Empty;
        try
        {
            string splashPath = configuration["SplashPath"] ?? "/opt/plainsight/splash.png";
            string splashDir = Path.GetDirectoryName(splashPath) ?? "/opt/plainsight";
            string versionFile = Path.Combine(splashDir, SplashVersionFile);
            string currentVersion = GetApplicationVersion();

            if (!Directory.Exists(splashDir))
            {
                Directory.CreateDirectory(splashDir);
            }

            if (File.Exists(splashPath) && File.Exists(versionFile))
            {
                string storedVersion = File.ReadAllText(versionFile).Trim();
                if (storedVersion == currentVersion)
                {
                    logger.LogInformation("Splash already current, skipping regeneration");
                    return;
                }
            }

            string hostname = Environment.MachineName;
            string displayVersion = GetDisplayVersion();
            tempPath = Path.Combine(splashDir, "splash.png.tmp");

            RenderSplash(tempPath, hostname, displayVersion);

            // Atomic replace: overwrite ensures /splash.png is never momentarily missing.
            File.Move(tempPath, splashPath, overwrite: true);
            tempPath = string.Empty;

            File.WriteAllText(versionFile, currentVersion);
            logger.LogInformation("Generated splash screen at {Path}", splashPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate splash screen");
        }
        finally
        {
            // Clean up the temp file if the move never happened (exception mid-render).
            if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                    /* best-effort cleanup */
                }
            }
        }
    }

    private static void RenderSplash(string outputPath, string hostname, string version)
    {
        SKImageInfo info = new(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using SKSurface surface = SKSurface.Create(info);
        SKCanvas canvas = surface.Canvas;

        canvas.Clear(BackgroundColor);

        int centerX = Width / 2;
        int logoCenterY = (Height / 2) - 60;
        int titleBaselineY = logoCenterY + (LogoSize / 2) + 40 + 56;
        int subtitleBaselineY = titleBaselineY + 14 + 22;

        DrawLogo(canvas, centerX, logoCenterY);
        DrawCenteredText(canvas, "PlainSight", 56, SKFontStyle.Bold, SKColors.White, centerX, titleBaselineY);
        DrawCenteredText(canvas, "Digital Signage", 22, SKFontStyle.Normal, SubtleText, centerX, subtitleBaselineY);
        DrawDeviceInfo(canvas, hostname, version);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.OpenWrite(outputPath);
        data.SaveTo(stream);
    }

    // Mirrors the favicon.svg design: rounded-rect border (gradient) + concentric rings + center dot.
    // All dimensions are derived from the SVG's 32×32 viewBox scaled to LogoSize.
    private static void DrawLogo(SKCanvas canvas, int centerX, int centerY)
    {
        float scale = LogoSize / 32f;
        float half = LogoSize / 2f;

        // Rounded rect border: inset 2 SVG units, rx=7, gradient cyan→blue top-left to bottom-right
        float inset = 2f * scale;
        SKRect rect = new(centerX - half + inset, centerY - half + inset,
                          centerX + half - inset, centerY + half - inset);
        float rx = 7f * scale;
        using SKShader shader = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top),
            new SKPoint(rect.Right, rect.Bottom),
            [CyanAccent, BlueAccent],
            SKShaderTileMode.Clamp);
        using SKPaint border = new() { Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * scale, Shader = shader, IsAntialias = true };
        canvas.DrawRoundRect(rect, rx, rx, border);

        // Outer ring: r=11 SVG units, blue at 40% opacity
        using SKPaint outer = new() { Style = SKPaintStyle.Stroke, StrokeWidth = scale, Color = BlueAccent.WithAlpha(100), IsAntialias = true };
        canvas.DrawCircle(centerX, centerY, 11f * scale, outer);

        // Mid ring: r=7, cyan at 50% opacity
        using SKPaint mid = new() { Style = SKPaintStyle.Stroke, StrokeWidth = scale, Color = CyanAccent.WithAlpha(127), IsAntialias = true };
        canvas.DrawCircle(centerX, centerY, 7f * scale, mid);

        // Center dot: r=3.2, solid cyan
        using SKPaint dot = new() { Style = SKPaintStyle.Fill, Color = CyanAccent, IsAntialias = true };
        canvas.DrawCircle(centerX, centerY, 3.2f * scale, dot);
    }

    private static void DrawCenteredText(SKCanvas canvas, string text, float size, SKFontStyle style, SKColor color, float x, float y)
    {
        using SKTypeface typeface = ResolveTypeface(style);
        using SKFont font = new(typeface, size);
        using SKPaint paint = new() { Color = color, IsAntialias = true };

        SKTextAlign align = SKTextAlign.Center;
        canvas.DrawText(text, x, y, align, font, paint);
    }

    private static void DrawDeviceInfo(SKCanvas canvas, string hostname, string version)
    {
        using SKTypeface typeface = ResolveTypeface(SKFontStyle.Normal);
        using SKFont font = new(typeface, 18);
        using SKPaint paint = new() { Color = SubtleText, IsAntialias = true };
        canvas.DrawText($"{hostname}  ·  {version}", Width - Padding, Height - Padding, SKTextAlign.Right, font, paint);
    }

    private static SKTypeface ResolveTypeface(SKFontStyle style)
    {
        string[] preferred = ["DejaVu Sans", "Liberation Sans", "Arial", "Segoe UI", "Helvetica"];
        foreach (string name in preferred)
        {
            SKTypeface? candidate = SKTypeface.FromFamilyName(name, style);
            if (candidate is not null && !string.Equals(candidate.FamilyName, "", StringComparison.Ordinal))
            {
                return candidate;
            }
        }
        return SKTypeface.Default;
    }

    private static string GetApplicationVersion()
    {
        string? version = typeof(SplashGeneratorService).Assembly.GetName().Version?.ToString();
        return version ?? "unknown";
    }

    private static string GetDisplayVersion()
    {
        Version? v = typeof(SplashGeneratorService).Assembly.GetName().Version;
        return v is not null ? $"v{v.Major}.{v.Minor}.{v.Build}" : "v?";
    }
}
