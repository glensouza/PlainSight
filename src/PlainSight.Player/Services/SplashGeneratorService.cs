using System.Drawing;
using System.Drawing.Drawing2D;

namespace PlainSight.Player.Services;

public class SplashGeneratorService(
    IConfiguration configuration,
    ILogger<SplashGeneratorService> logger) : IHostedService
{
    private const int Width = 1920;
    private const int Height = 1080;
    private const string SplashVersionFile = "splash.version";
    private const int LogoSize = 240;
    private const int Padding = 32;

    public Task StartAsync(CancellationToken cancellationToken)
    {
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
            string tempPath = Path.Combine(splashDir, "splash.png.tmp");

#pragma warning disable CA1416
            using (Bitmap bitmap = new(Width, Height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.FromArgb(10, 10, 20));
                graphics.SmoothingMode = SmoothingMode.AntiAlias;

                this.DrawLogo(graphics);
                this.DrawText(graphics);
                this.DrawHostname(graphics, hostname);

                bitmap.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);
            }
#pragma warning restore CA1416

            if (File.Exists(splashPath))
            {
                File.Delete(splashPath);
            }
            File.Move(tempPath, splashPath);

            File.WriteAllText(versionFile, currentVersion);
            logger.LogInformation("Generated splash screen at {Path}", splashPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate splash screen");
        }
    }

    private void DrawLogo(Graphics graphics)
    {
#pragma warning disable CA1416
        int centerX = Width / 2;
        int centerY = Height / 2 - 80;

        using (Pen pen = new(Color.FromArgb(34, 211, 238), 3))
        {
            int outerRadius = LogoSize / 2;
            graphics.DrawEllipse(pen, centerX - outerRadius, centerY - outerRadius, outerRadius * 2, outerRadius * 2);
        }

        using (Pen pen = new(Color.FromArgb(34, 211, 238, 127), 2))
        {
            int midRadius = LogoSize / 3;
            graphics.DrawEllipse(pen, centerX - midRadius, centerY - midRadius, midRadius * 2, midRadius * 2);
        }

        using (Pen pen = new(Color.FromArgb(59, 130, 246, 100), 2))
        {
            int innerRadius = LogoSize / 5;
            graphics.DrawEllipse(pen, centerX - innerRadius, centerY - innerRadius, innerRadius * 2, innerRadius * 2);
        }

        using (Brush brush = new SolidBrush(Color.FromArgb(34, 211, 238)))
        {
            graphics.FillEllipse(brush, centerX - 8, centerY - 8, 16, 16);
        }
#pragma warning restore CA1416
    }

    private void DrawText(Graphics graphics)
    {
#pragma warning disable CA1416
        int centerX = Width / 2;
        int centerY = Height / 2 + 60;

        using (Font titleFont = new("Arial", 56, FontStyle.Bold))
        using (Brush titleBrush = new SolidBrush(Color.White))
        using (StringFormat format = new() { Alignment = StringAlignment.Center })
        {
            graphics.DrawString("PlainSight", titleFont, titleBrush, centerX, centerY, format);
        }

        using (Font subtitleFont = new("Arial", 22))
        using (Brush subtitleBrush = new SolidBrush(Color.FromArgb(140, 140, 165)))
        using (StringFormat format = new() { Alignment = StringAlignment.Center })
        {
            graphics.DrawString("Digital Signage", subtitleFont, subtitleBrush, centerX, centerY + 70, format);
        }
#pragma warning restore CA1416
    }

    private void DrawHostname(Graphics graphics, string hostname)
    {
#pragma warning disable CA1416
        using (Font hostnameFont = new("Arial", 16))
        using (Brush hostnameBrush = new SolidBrush(Color.FromArgb(140, 140, 165)))
        using (StringFormat format = new() { Alignment = StringAlignment.Far })
        {
            graphics.DrawString(
                hostname,
                hostnameFont,
                hostnameBrush,
                Width - Padding,
                Height - Padding - 20,
                format);
        }
#pragma warning restore CA1416
    }

    private static string GetApplicationVersion()
    {
        string? version = typeof(SplashGeneratorService).Assembly.GetName().Version?.ToString();
        return version ?? "unknown";
    }
}
