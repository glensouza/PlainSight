namespace PlainSight.Server.Services;

// Single source of truth for the serve-time view of a playlist: applies the playlist's SortMode
// and drops items whose backing ContentItem or Announcement has expired. Both the heartbeat
// response (DeviceApi) and the admin preview (PlaylistPreviewDialog) project from this so they
// never drift apart on ordering or expiration rules.
public static class PlaylistOrdering
{
    public static IEnumerable<PlaylistItem> SortAndFilter(Playlist playlist, DateTime utcNow)
    {
        IEnumerable<PlaylistItem> sorted = playlist.SortMode == PlaylistSortMode.ByEventDate
            ? playlist.Items
                // Sort by the announcement's event date; nulls sort last.
                .OrderBy(i => i.Announcement.EventDate ?? DateOnly.MaxValue)
                .ThenBy(i => i.Order)
            : playlist.Items.OrderBy(i => i.Order);

        // Serve an announcement through the end of its event day (local); no date never expires.
        return sorted.Where(i => !i.Announcement.EventDate.IsExpiredOn(utcNow));
    }
}
