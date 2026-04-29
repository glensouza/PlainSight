using Microsoft.EntityFrameworkCore;
using Signage.Shared.Models;

namespace Signage.Server.Data;

public class SignageDbContext(DbContextOptions<SignageDbContext> options) : DbContext(options)
{
    public DbSet<Device> Devices => this.Set<Device>();
    public DbSet<ContentItem> ContentItems => this.Set<ContentItem>();
    public DbSet<Playlist> Playlists => this.Set<Playlist>();
    public DbSet<PlaylistItem> PlaylistItems => this.Set<PlaylistItem>();
    public DbSet<PlayerVersion> PlayerVersions => this.Set<PlayerVersion>();
    public DbSet<DeviceGroupVersion> DeviceGroupVersions => this.Set<DeviceGroupVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Device>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DeviceId).IsUnique();
        });

        modelBuilder.Entity<ContentItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.FileName).IsUnique();
        });

        modelBuilder.Entity<Playlist>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasMany(e => e.Items)
                .WithOne(e => e.Playlist)
                .HasForeignKey(e => e.PlaylistId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlaylistItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.ContentItem)
                .WithMany()
                .HasForeignKey(e => e.ContentItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PlayerVersion>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.VersionNumber).IsUnique();
        });

        modelBuilder.Entity<DeviceGroupVersion>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.GroupName).IsUnique();

            // Seed Default group
            entity.HasData(new DeviceGroupVersion
            {
                Id = 1,
                GroupName = "Default",
                TargetVersion = "1.0.0"
            });
        });
    }
}
