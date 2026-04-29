using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signage.Server.Data;
using Signage.Shared.Models;

namespace Signage.Server.Controllers;

[ApiController]
[Route("api/updates")]
public class UpdatesController(
    SignageDbContext context,
    IConfiguration configuration,
    ILogger<UpdatesController> logger) : ControllerBase
{
    private string UpdatesPath => configuration["UpdatesPath"] ?? "/mnt/signage/updates";

    [HttpGet("{version}/binary")]
    public async Task<IActionResult> DownloadBinary(string version, CancellationToken cancellationToken)
    {
        PlayerVersion? record = await context.PlayerVersions
            .FirstOrDefaultAsync(v => v.VersionNumber == version, cancellationToken);

        if (record == null)
            return this.NotFound();

        string filePath = Path.Combine(UpdatesPath, record.FileName);
        if (!System.IO.File.Exists(filePath))
        {
            logger.LogError("Binary file missing for version {Version}: {Path}", version, filePath);
            return this.NotFound();
        }

        return this.PhysicalFile(filePath, "application/octet-stream", record.FileName);
    }
}
