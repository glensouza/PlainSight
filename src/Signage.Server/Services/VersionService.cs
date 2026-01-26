namespace Signage.Server.Services;

public class VersionService(ILogger<VersionService> logger)
{
    public string GetTargetVersion(string deviceGroup)
    {
        // TODO: Implement database-driven version management for canary deployments
        // This should query a configuration table or version assignment table
        // Example: SELECT TargetVersion FROM DeviceGroupVersions WHERE GroupName = @deviceGroup
        logger.LogWarning("Using hardcoded version - implement database-driven version management for production");
        return "1.0.0";
    }
}
