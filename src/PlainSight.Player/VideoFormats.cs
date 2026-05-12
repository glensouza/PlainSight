using PlainSight.Shared;

namespace PlainSight.Player;

internal static class VideoFormats
{
    internal static readonly string[] SupportedMediaExtensions = MediaConstants.AllSupportedExtensions;

    internal static readonly Dictionary<string, string> ContentTypes = new()
    {
        { ".mp4",  "video/mp4" },
        { ".webm", "video/webm" },
        { ".mkv",  "video/x-matroska" },
        { ".avi",  "video/x-msvideo" },
        { ".mov",  "video/quicktime" },
        { ".m4v",  "video/mp4" },
        { ".ts",   "video/mp2t" },
        { ".png",  "image/png" },
        { ".jpg",  "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".gif",  "image/gif" },
        { ".bmp",  "image/bmp" },
        { ".webp", "image/webp" }
    };

    internal static bool IsVideo(string filename) => MediaConstants.IsVideo(filename);

    internal static bool IsImage(string filename) => MediaConstants.IsImage(filename);
}
