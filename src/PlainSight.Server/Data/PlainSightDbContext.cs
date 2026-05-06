using Microsoft.EntityFrameworkCore;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Data;

public class PlainSightDbContext(DbContextOptions<PlainSightDbContext> options) : DbContext(options)
{
    public DbSet<Device> Devices => this.Set<Device>();
    public DbSet<DeviceScreenshot> DeviceScreenshots => this.Set<DeviceScreenshot>();
    public DbSet<ContentItem> ContentItems => this.Set<ContentItem>();
    public DbSet<Playlist> Playlists => this.Set<Playlist>();
    public DbSet<PlaylistItem> PlaylistItems => this.Set<PlaylistItem>();
    public DbSet<PlayerVersion> PlayerVersions => this.Set<PlayerVersion>();
    public DbSet<DeviceGroupVersion> DeviceGroupVersions => this.Set<DeviceGroupVersion>();
    public DbSet<DeviceGroup> DeviceGroups => this.Set<DeviceGroup>();
    public DbSet<AdminUser> AdminUsers => this.Set<AdminUser>();
    public DbSet<Schedule> Schedules => this.Set<Schedule>();
    public DbSet<ScheduleTargetGroup> ScheduleTargetGroups => this.Set<ScheduleTargetGroup>();
    public DbSet<NdiSource> NdiSources => this.Set<NdiSource>();
    public DbSet<SystemSetting> SystemSettings => this.Set<SystemSetting>();
    public DbSet<LogEntry> LogEntries => this.Set<LogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.HasKey(e => e.Key);
        });

        modelBuilder.Entity<Device>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DeviceId).IsUnique();
            entity.HasOne(e => e.AssignedNdiSource)
                .WithMany(s => s.AssignedDevices)
                .HasForeignKey(e => e.AssignedNdiSourceId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<NdiSource>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ServiceName).IsUnique();
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
            
            entity.HasMany(e => e.TargetGroups)
                .WithOne(e => e.Schedule)
                .HasForeignKey(e => e.ScheduleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlaylistItem>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<DeviceGroupVersion>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.GroupName).IsUnique();
        });

        modelBuilder.Entity<DeviceGroup>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasData(new DeviceGroup { Id = 1, Name = "Default" });
        });

        modelBuilder.Entity<AdminUser>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
        });

        modelBuilder.Entity<DeviceScreenshot>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.DeviceId, e.CapturedAt });
            entity.HasOne(e => e.Device)
                .WithMany()
                .HasForeignKey(e => e.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LogEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => new { e.Category, e.LevelOrder, e.Timestamp });
            entity.HasIndex(e => new { e.SourceId, e.Timestamp });
            entity.Property(e => e.SourceId).HasMaxLength(200);
            entity.Property(e => e.CategoryName).HasMaxLength(200);
            entity.Property(e => e.Level).HasMaxLength(50);
            entity.Property(e => e.Message).HasMaxLength(2000);
            entity.Property(e => e.Exception).HasMaxLength(8000);
        });
    }
}
