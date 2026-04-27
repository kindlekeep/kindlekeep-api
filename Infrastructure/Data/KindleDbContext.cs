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
        // Complex logic: Relies entirely on the pre-compiled Native AOT models.
        // Extraneous configuration here will trigger runtime reflection and model discovery, 
        // which causes the binary to crash.
        base.OnModelCreating(modelBuilder);
    }
}