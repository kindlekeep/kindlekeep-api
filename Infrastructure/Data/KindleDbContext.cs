using KindleKeep.Api.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace KindleKeep.Api.Infrastructure.Data;

public class KindleDbContext(DbContextOptions<KindleDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<MonitorTarget> MonitorTargets => Set<MonitorTarget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ExternalId).IsUnique();
        });

        modelBuilder.Entity<MonitorTarget>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Bypasses reflection-based JSON serializers for AOT compatibility
            entity.Property(e => e.RequestHeaders)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.User)
                .WithMany(u => u.Monitors)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}