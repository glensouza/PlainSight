using Microsoft.EntityFrameworkCore;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Data;

public class PlainSightDbContext(DbContextOptions<PlainSightDbContext> options) : DbContext(options)
{
    public DbSet<Device> Devices => this.Set<Device>();
    public DbSet<ContentItem> ContentItems => this.Set<ContentItem>();
    public DbSet<Playlist> Playlists => this.Set<Playlist>();
    public DbSet<PlaylistItem> PlaylistItems => this.Set<PlaylistItem>();
    public DbSet<Schedule> Schedules => this.Set<Schedule>();
    public DbSet<PlayerVersion> PlayerVersions => this.Set<PlayerVersion>();
    public DbSet<DeviceGroupVersion> DeviceGroupVersions => this.Set<DeviceGroupVersion>();
    public DbSet<AdminUser> AdminUsers => this.Set<AdminUser>();

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

        modelBuilder.Entity<Schedule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Playlist)
                .WithMany()
                .HasForeignKey(e => e.PlaylistId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScheduleTargetGroup>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Schedule)
                .WithMany(s => s.TargetGroups)
                .HasForeignKey(e => e.ScheduleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.ScheduleId, e.GroupName }).IsUnique();
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

            entity.HasOne(e => e.DefaultPlaylist)
                .WithMany()
                .HasForeignKey(e => e.DefaultPlaylistId)
                .OnDelete(DeleteBehavior.SetNull);

            // Seed Default group
            entity.HasData(new DeviceGroupVersion
            {
                Id = 1,
                GroupName = "Default",
                TargetVersion = "1.0.0"
            });
        });

        modelBuilder.Entity<AdminUser>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
        });
    }
}
