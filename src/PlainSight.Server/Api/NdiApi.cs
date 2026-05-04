using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Api;

public static class NdiApi
{
    private sealed record DeviceLiveConfigDto(bool NdiAutoSwitch, int? AssignedNdiSourceId, bool? LiveModeOverride);
    private sealed record CreateNdiSourceDto(string ServiceName, string? HostName, string? IpAddress, int Port = 5960);

    public static RouteGroupBuilder MapNdiApi(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/ndi").RequireAuthorization();

        group.MapGet("/sources", async (PlainSightDbContext context, CancellationToken ct) =>
        {
            List<NdiSource> sources = await context.NdiSources
                .OrderBy(s => s.ServiceName)
                .ToListAsync(ct);
            return Results.Ok(sources);
        });

        group.MapPost("/sources", async (
            [FromBody] CreateNdiSourceDto dto,
            PlainSightDbContext context,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            ILogger logger = loggerFactory.CreateLogger("NdiApi");

            if (string.IsNullOrWhiteSpace(dto.ServiceName))
            {
                return Results.BadRequest("ServiceName is required.");
            }

            bool exists = await context.NdiSources.AnyAsync(s => s.ServiceName == dto.ServiceName.Trim(), ct);
            if (exists)
            {
                return Results.Conflict("An NDI source with that name already exists.");
            }

            DateTime now = DateTime.UtcNow;
            NdiSource source = new()
            {
                ServiceName = dto.ServiceName.Trim(),
                HostName = dto.HostName?.Trim(),
                IpAddress = dto.IpAddress?.Trim(),
                Port = dto.Port,
                FirstSeenUtc = now,
                LastSeenUtc = now,
                IsManual = true
            };

            context.NdiSources.Add(source);
            await context.SaveChangesAsync(ct);

            logger.LogInformation("Manually added NDI source: {ServiceName}", source.ServiceName);
            return Results.Created($"/api/ndi/sources/{source.Id}", source);
        });

        group.MapDelete("/sources/{id:int}", async (
            int id,
            PlainSightDbContext context,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            ILogger logger = loggerFactory.CreateLogger("NdiApi");

            NdiSource? source = await context.NdiSources.FindAsync([id], ct);
            if (source == null)
            {
                return Results.NotFound();
            }

            context.NdiSources.Remove(source);
            await context.SaveChangesAsync(ct);

            logger.LogInformation("Deleted NDI source: {ServiceName}", source.ServiceName);
            return Results.NoContent();
        });

        group.MapGet("/devices/{deviceId}/livemode", async (
            string deviceId,
            PlainSightDbContext context,
            CancellationToken ct) =>
        {
            Device? device = await context.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);
            if (device == null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new DeviceLiveConfigDto(
                device.NdiAutoSwitch,
                device.AssignedNdiSourceId,
                device.LiveModeOverride));
        });

        group.MapPut("/devices/{deviceId}/livemode", async (
            string deviceId,
            [FromBody] DeviceLiveConfigDto dto,
            PlainSightDbContext context,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            ILogger logger = loggerFactory.CreateLogger("NdiApi");
            Device? device = await context.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);
            if (device == null)
            {
                return Results.NotFound();
            }

            if (dto.AssignedNdiSourceId.HasValue)
            {
                bool sourceExists = await context.NdiSources
                    .AnyAsync(s => s.Id == dto.AssignedNdiSourceId.Value, ct);
                if (!sourceExists)
                {
                    return Results.BadRequest("Assigned NDI source not found.");
                }
            }

            device.NdiAutoSwitch = dto.NdiAutoSwitch;
            device.AssignedNdiSourceId = dto.AssignedNdiSourceId;
            device.LiveModeOverride = dto.LiveModeOverride;

            await context.SaveChangesAsync(ct);
            logger.LogInformation(
                "Updated live-mode config for device {DeviceId}: auto={Auto}, sourceId={SourceId}, override={Override}",
                deviceId, dto.NdiAutoSwitch, dto.AssignedNdiSourceId, dto.LiveModeOverride);

            return Results.Ok();
        });

        return group;
    }
}
