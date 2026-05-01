namespace PlainSight.Player;

internal static class VideoFormats
{
    internal static readonly string[] SupportedMediaExtensions = [".mp4", ".webm", ".mkv", ".avi", ".mov", ".png", ".jpg", ".jpeg"];

    internal static readonly Dictionary<string, string> ContentTypes = new()
    {
        { ".mp4",  "video/mp4" },
        { ".webm", "video/webm" },
        { ".mkv",  "video/x-matroska" },
        { ".avi",  "video/x-msvideo" },
        { ".mov",  "video/quicktime" },
        { ".png",  "image/png" },
        { ".jpg",  "image/jpeg" },
        { ".jpeg", "image/jpeg" }
    };

    internal static bool IsVideo(string filename)
    {
        string extension = Path.GetExtension(filename).ToLowerInvariant();
        return extension is ".mp4" or ".webm" or ".mkv" or ".avi" or ".mov";
    }

    internal static bool IsImage(string filename)
    {
        string extension = Path.GetExtension(filename).ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg";
    }
}
