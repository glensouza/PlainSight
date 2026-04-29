namespace Signage.Player;

internal static class VideoFormats
{
    internal static readonly string[] SupportedExtensions = [".mp4", ".webm", ".mkv", ".avi", ".mov"];

    internal static readonly Dictionary<string, string> ContentTypes = new()
    {
        { ".mp4",  "video/mp4" },
        { ".webm", "video/webm" },
        { ".mkv",  "video/x-matroska" },
        { ".avi",  "video/x-msvideo" },
        { ".mov",  "video/quicktime" }
    };
}
