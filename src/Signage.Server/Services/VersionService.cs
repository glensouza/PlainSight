using Signage.Shared.Models;

namespace Signage.Server.Services;

public class VersionService
{
    private readonly ILogger<VersionService> _logger;

    public VersionService(ILogger<VersionService> logger)
    {
        _logger = logger;
    }

    public string GetTargetVersion(string deviceGroup)
    {
        // This would typically query a database or configuration
        // For now, return a default version
        return "1.0.0";
    }
}
