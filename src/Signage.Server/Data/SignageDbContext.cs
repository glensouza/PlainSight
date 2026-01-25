using Microsoft.EntityFrameworkCore;
using Signage.Shared.Models;

namespace Signage.Server.Data;

public class SignageDbContext : DbContext
{
    public SignageDbContext(DbContextOptions<SignageDbContext> options) : base(options)
    {
    }

    public DbSet<Device> Devices => Set<Device>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Device>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DeviceId).IsUnique();
        });
    }
}
