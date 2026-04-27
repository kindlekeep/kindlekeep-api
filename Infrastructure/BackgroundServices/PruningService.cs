using KindleKeep.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KindleKeep.Api.Infrastructure.BackgroundServices;

public class PruningService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalHours = configuration.GetValue<int>("Pruning:IntervalHours", 24);
        var delay = TimeSpan.FromHours(intervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            await PruneDataAsync(stoppingToken);
            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task PruneDataAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<KindleDbContext>();

        var retentionDays = configuration.GetValue<int>("Pruning:RetentionDays", 7);
        var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);

        // Complex logic: The parameter array overload explicitly segregates SQL parameters from the cancellation token, 
        // preventing the runtime from interpreting the token as a database bind variable.
        await dbContext.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"UptimeLogs\" WHERE \"Timestamp\" < {0}", 
            [cutoffDate], 
            cancellationToken: stoppingToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"SecurityAudits\" WHERE \"CreatedAt\" < {0}", 
            [cutoffDate], 
            cancellationToken: stoppingToken);
    }
}