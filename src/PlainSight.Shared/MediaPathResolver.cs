namespace PlainSight.Shared;

public static class MediaPathResolver
{
    public static string Resolve(string configuredPath)
    {
        ArgumentNullException.ThrowIfNull(configuredPath);

        if (configuredPath.Length == 0)
        {
            return configuredPath;
        }

        string normalized = Path.TrimEndingDirectorySeparator(configuredPath);
        if (Directory.Exists(normalized))
        {
            return normalized;
        }

        string root = Path.GetPathRoot(normalized) ?? string.Empty;
        string remainder = root.Length == 0 ? normalized : normalized[root.Length..];
        string[] segments = remainder.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        string current = root.Length > 0 ? root : ".";
        if (root.Length > 0 && !Directory.Exists(current))
        {
            return configuredPath;
        }

        foreach (string segment in segments)
        {
            string candidate = Path.Combine(current, segment);
            if (Directory.Exists(candidate))
            {
                current = candidate;
                continue;
            }

            string? match = Directory.GetDirectories(current)
                .FirstOrDefault(d => string.Equals(Path.GetFileName(d), segment, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                return configuredPath;
            }

            current = match;
        }

        return current;
    }
}
