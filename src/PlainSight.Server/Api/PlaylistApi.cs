using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Api;

public static class PlaylistApi
{
    public static RouteGroupBuilder MapPlaylistApi(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/playlists");

        group.MapGet("/", async (PlainSightDbContext context, CancellationToken ct) =>
        {
            List<Playlist> playlists = await context.Playlists
                .Include(p => p.Items)
                .ThenInclude(i => i.ContentItem)
                .OrderByDescending(p => p.UpdatedAt)
                .ToListAsync(ct);
            
            return Results.Ok(playlists);
        });

        group.MapGet("/{id:int}", async (int id, PlainSightDbContext context, CancellationToken ct) =>
        {
            Playlist? playlist = await context.Playlists
                .Include(p => p.Items)
                .ThenInclude(i => i.ContentItem)
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            return playlist is not null ? Results.Ok(playlist) : Results.NotFound();
        });

        group.MapGet("/{playlistId:int}/export", async (int playlistId, PlainSightDbContext context) =>
        {
            Playlist? playlist = await context.Playlists
                .Include(p => p.Items)
                .ThenInclude(i => i.ContentItem)
                .FirstOrDefaultAsync(p => p.Id == playlistId);

            if (playlist == null)
                return Results.NotFound();

            return Results.Ok(new
            {
                name = playlist.Name,
                items = playlist.Items.OrderBy(i => i.Order).Select(i => new
                {
                    filename = i.ContentItem.FileName,
                    duration = i.OverrideDurationSeconds ?? i.ContentItem.DurationSeconds,
                    type = i.ContentItem.Type.ToString().ToLower()
                }).ToList()
            });
        });

        return group;
    }
}
