using Microsoft.EntityFrameworkCore;
using Signage.Server.Data;
using Signage.Shared.Models;

namespace Signage.Server.Api;

public static class PlaylistApi
{
    public static RouteGroupBuilder MapPlaylistApi(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/playlists");

        group.MapGet("/", async (SignageDbContext context, CancellationToken ct) =>
        {
            List<Playlist> playlists = await context.Playlists
                .Include(p => p.Items)
                .ThenInclude(i => i.ContentItem)
                .OrderByDescending(p => p.UpdatedAt)
                .ToListAsync(ct);
            
            return Results.Ok(playlists);
        });

        group.MapGet("/{id:int}", async (int id, SignageDbContext context, CancellationToken ct) =>
        {
            Playlist? playlist = await context.Playlists
                .Include(p => p.Items)
                .ThenInclude(i => i.ContentItem)
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            return playlist is not null ? Results.Ok(playlist) : Results.NotFound();
        });

        group.MapPost("/", async (PlaylistCreateDto dto, SignageDbContext context, ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("PlaylistApi");
            try
            {
                Playlist playlist = new()
                {
                    Name = dto.Name,
                    Description = dto.Description,
                    IsActive = dto.IsActive,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                context.Playlists.Add(playlist);
                await context.SaveChangesAsync();

                return Results.Ok(playlist);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating playlist");
                return Results.Problem("Error creating playlist", statusCode: 500);
            }
        });

        group.MapPut("/{id:int}", async (int id, PlaylistCreateDto dto, SignageDbContext context, ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("PlaylistApi");
            Playlist? playlist = await context.Playlists.FindAsync(id);
            if (playlist == null)
                return Results.NotFound();

            try
            {
                playlist.Name = dto.Name;
                playlist.Description = dto.Description;
                playlist.IsActive = dto.IsActive;
                playlist.UpdatedAt = DateTime.UtcNow;

                await context.SaveChangesAsync();
                return Results.Ok(playlist);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating playlist");
                return Results.Problem("Error updating playlist", statusCode: 500);
            }
        });

        group.MapDelete("/{id:int}", async (int id, SignageDbContext context, ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("PlaylistApi");
            Playlist? playlist = await context.Playlists.FindAsync(id);
            if (playlist == null)
                return Results.NotFound();

            try
            {
                context.Playlists.Remove(playlist);
                await context.SaveChangesAsync();
                return Results.Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting playlist");
                return Results.Problem("Error deleting playlist", statusCode: 500);
            }
        });

        group.MapPost("/{playlistId:int}/items", async (int playlistId, AddPlaylistItemDto dto, SignageDbContext context, ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("PlaylistApi");
            Playlist? playlist = await context.Playlists
                .Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.Id == playlistId);

            if (playlist == null)
                return Results.NotFound("Playlist not found");

            ContentItem? content = await context.ContentItems.FindAsync(dto.ContentItemId);
            if (content == null)
                return Results.NotFound("Content item not found");

            try
            {
                int nextOrder = playlist.Items.Any() ? playlist.Items.Max(i => i.Order) + 1 : 1;

                PlaylistItem playlistItem = new()
                {
                    PlaylistId = playlistId,
                    ContentItemId = dto.ContentItemId,
                    Order = nextOrder,
                    OverrideDurationSeconds = dto.OverrideDurationSeconds
                };

                context.PlaylistItems.Add(playlistItem);
                playlist.UpdatedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();

                return Results.Ok(playlistItem);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error adding item to playlist");
                return Results.Problem("Error adding item to playlist", statusCode: 500);
            }
        });

        group.MapDelete("/items/{itemId:int}", async (int itemId, SignageDbContext context, ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("PlaylistApi");
            PlaylistItem? item = await context.PlaylistItems
                .Include(i => i.Playlist)
                .FirstOrDefaultAsync(i => i.Id == itemId);

            if (item == null)
                return Results.NotFound();

            try
            {
                context.PlaylistItems.Remove(item);
                item.Playlist.UpdatedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();
                return Results.Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error removing item from playlist");
                return Results.Problem("Error removing item from playlist", statusCode: 500);
            }
        });

        group.MapGet("/{playlistId:int}/export", async (int playlistId, SignageDbContext context) =>
        {
            Playlist? playlist = await context.Playlists
                .Include(p => p.Items)
                .ThenInclude(i => i.ContentItem)
                .FirstOrDefaultAsync(p => p.Id == playlistId);

            if (playlist == null)
                return Results.NotFound();

            // Generate playlist.json format for the player
            var playlistJson = new
            {
                name = playlist.Name,
                items = playlist.Items.OrderBy(i => i.Order).Select(i => new
                {
                    filename = i.ContentItem.FileName,
                    duration = i.OverrideDurationSeconds ?? i.ContentItem.DurationSeconds,
                    type = i.ContentItem.Type.ToString().ToLower()
                }).ToList()
            };

            return Results.Ok(playlistJson);
        });

        return group;
    }
}

public record PlaylistCreateDto(string Name, string? Description, bool IsActive);
public record AddPlaylistItemDto(int ContentItemId, int? OverrideDurationSeconds);
