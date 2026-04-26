using KindleKeep.Api.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace KindleKeep.Api.Infrastructure.Data;

public class KindleDbContext(DbContextOptions<KindleDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<MonitorTarget> MonitorTargets => Set<MonitorTarget>();
    public DbSet<UptimeLog> UptimeLogs => Set<UptimeLog>();
    public DbSet<SecurityAudit> SecurityAudits => Set<SecurityAudit>();
    public DbSet<AlertIncident> AlertIncidents => Set<AlertIncident>();

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

            entity.Property(e => e.RequestHeaders)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.User)
                .WithMany(u => u.Monitors)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UptimeLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp);

            entity.HasOne(e => e.Monitor)
                .WithMany()
                .HasForeignKey(e => e.MonitorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SecurityAudit>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.RawHeaders)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.Monitor)
                .WithMany()
                .HasForeignKey(e => e.MonitorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AlertIncident>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            // Indexing optimizes database lookup speeds for historical audits
            entity.HasIndex(e => e.IncidentHash);

            entity.HasOne(e => e.Monitor)
                .WithMany()
                .HasForeignKey(e => e.MonitorId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}