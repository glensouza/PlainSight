using Microsoft.EntityFrameworkCore;
using Signage.Shared.Models;

namespace Signage.Server.Data;

public class SignageDbContext(DbContextOptions<SignageDbContext> options) : DbContext(options)
{
    public DbSet<Device> Devices => this.Set<Device>();

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
