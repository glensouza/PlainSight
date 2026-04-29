using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signage.Server.Data;
using Signage.Shared.Models;

namespace Signage.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PlaylistsController(
    SignageDbContext context,
    ILogger<PlaylistsController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPlaylists()
    {
        List<Playlist> playlists = await context.Playlists
            .Include(p => p.Items)
            .ThenInclude(i => i.ContentItem)
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync();
        
        return this.Ok(playlists);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetPlaylistById(int id)
    {
        Playlist? playlist = await context.Playlists
            .Include(p => p.Items)
            .ThenInclude(i => i.ContentItem)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (playlist == null)
            return this.NotFound();

        return this.Ok(playlist);
    }

    [HttpPost]
    public async Task<IActionResult> CreatePlaylist([FromBody] PlaylistCreateDto dto)
    {
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

            return this.Ok(playlist);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating playlist");
            return this.StatusCode(500, "Error creating playlist");
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePlaylist(int id, [FromBody] PlaylistCreateDto dto)
    {
        Playlist? playlist = await context.Playlists.FindAsync(id);
        if (playlist == null)
            return this.NotFound();

        try
        {
            playlist.Name = dto.Name;
            playlist.Description = dto.Description;
            playlist.IsActive = dto.IsActive;
            playlist.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();
            return this.Ok(playlist);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating playlist");
            return this.StatusCode(500, "Error updating playlist");
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePlaylist(int id)
    {
        Playlist? playlist = await context.Playlists.FindAsync(id);
        if (playlist == null)
            return this.NotFound();

        try
        {
            context.Playlists.Remove(playlist);
            await context.SaveChangesAsync();
            return this.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting playlist");
            return this.StatusCode(500, "Error deleting playlist");
        }
    }

    [HttpPost("{playlistId}/items")]
    public async Task<IActionResult> AddItemToPlaylist(int playlistId, [FromBody] AddPlaylistItemDto dto)
    {
        Playlist? playlist = await context.Playlists
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == playlistId);

        if (playlist == null)
            return this.NotFound("Playlist not found");

        ContentItem? content = await context.ContentItems.FindAsync(dto.ContentItemId);
        if (content == null)
            return this.NotFound("Content item not found");

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

            return this.Ok(playlistItem);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding item to playlist");
            return this.StatusCode(500, "Error adding item to playlist");
        }
    }

    [HttpDelete("items/{itemId}")]
    public async Task<IActionResult> RemoveItemFromPlaylist(int itemId)
    {
        PlaylistItem? item = await context.PlaylistItems
            .Include(i => i.Playlist)
            .FirstOrDefaultAsync(i => i.Id == itemId);

        if (item == null)
            return this.NotFound();

        try
        {
            context.PlaylistItems.Remove(item);
            item.Playlist.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
            return this.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing item from playlist");
            return this.StatusCode(500, "Error removing item from playlist");
        }
    }

    [HttpGet("{playlistId}/export")]
    public async Task<IActionResult> ExportPlaylistJson(int playlistId)
    {
        Playlist? playlist = await context.Playlists
            .Include(p => p.Items)
            .ThenInclude(i => i.ContentItem)
            .FirstOrDefaultAsync(p => p.Id == playlistId);

        if (playlist == null)
            return this.NotFound();

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

        return this.Ok(playlistJson);
    }
}

public record PlaylistCreateDto(string Name, string? Description, bool IsActive);
public record AddPlaylistItemDto(int ContentItemId, int? OverrideDurationSeconds);
