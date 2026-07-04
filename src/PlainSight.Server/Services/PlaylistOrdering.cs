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
                // Coalesce across the XOR target so a ContentItem-backed and an Announcement-backed
                // item interleave by their actual event date; nulls sort last.
                .OrderBy(i => i.ContentItem?.EventDate ?? i.Announcement?.EventDate ?? DateOnly.MaxValue)
                .ThenBy(i => i.Order)
            : playlist.Items.OrderBy(i => i.Order);

        return sorted.Where(i => !(i.Announcement != null && i.Announcement.ExpiresAt < utcNow)
            && !(i.ContentItem != null && i.ContentItem.ExpiresAt < utcNow));
    }
}
