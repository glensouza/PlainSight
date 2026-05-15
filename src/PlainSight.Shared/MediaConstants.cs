namespace PlainSight.Shared;

public static class MediaConstants
{
    public static readonly string[] VideoExtensions = [".mp4", ".webm", ".mkv", ".avi", ".mov", ".m4v", ".ts"];
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"];

    public static readonly string[] AllSupportedExtensions = [.. VideoExtensions, .. ImageExtensions];

    public static bool IsSupported(string filename)
    {
        string extension = Path.GetExtension(filename).ToLowerInvariant();
        return AllSupportedExtensions.Contains(extension);
    }

    public static bool IsVideo(string filename)
    {
        string extension = Path.GetExtension(filename).ToLowerInvariant();
        return VideoExtensions.Contains(extension);
    }

    public static bool IsImage(string filename)
    {
        string extension = Path.GetExtension(filename).ToLowerInvariant();
        return ImageExtensions.Contains(extension);
    }
}
