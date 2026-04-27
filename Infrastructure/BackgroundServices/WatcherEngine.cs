using System.Diagnostics;
using System.Text.Json;
using KindleKeep.Api.API.Hubs;
using KindleKeep.Api.Core.DTOs;
using KindleKeep.Api.Core.Entities;
using KindleKeep.Api.Core.Enums;
using KindleKeep.Api.Infrastructure.Alerting;
using KindleKeep.Api.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KindleKeep.Api.Infrastructure.BackgroundServices;

public class WatcherEngine(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IHubContext<PulseHub> hubContext,
    IConfiguration configuration,
    AlertManager alertManager) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = configuration.GetValue<int>("Watcher:IntervalMinutes", 1);
        var delay = TimeSpan.FromMinutes(intervalMinutes);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await ProcessTargetsAsync(stoppingToken);
                await Task.Delay(delay, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }
        catch (ObjectDisposedException)
        {
            // Scope disposed during execution
        }
    }

    private async Task ProcessTargetsAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<KindleDbContext>();
        var dataSource = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        var activeTargets = new List<MonitorTarget>();

        await using var connection = await dataSource.OpenConnectionAsync(stoppingToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"Id\", \"Url\", \"FriendlyName\", \"CurrentUptimeStatus\", \"CurrentSecurityGrade\", \"IsActive\", \"UserId\" FROM \"MonitorTargets\" WHERE \"IsActive\" = true";
        
        await using var reader = await command.ExecuteReaderAsync(stoppingToken);
        while (await reader.ReadAsync(stoppingToken))
        {
            // Complex logic: Manual instantiation strictly fulfills the required modifier contract 
            // ensuring CS9035 compiler validation passes during Native AOT publishing.
            activeTargets.Add(new MonitorTarget
            {
                Id = reader.GetGuid(0),
                Url = reader.GetString(1),
                FriendlyName = reader.GetString(2),
                CurrentUptimeStatus = (UptimeStatus)reader.GetInt32(3),
                CurrentSecurityGrade = reader.GetString(4)[0],
                IsActive = reader.GetBoolean(5),
                UserId = reader.GetGuid(6)
            });
        }

        var client = httpClientFactory.CreateClient("WatcherClient");

        foreach (var target in activeTargets)
        {
            var stopwatch = new Stopwatch();
            var status = UptimeStatus.Down;
            string? errorMessage = null;
            int? statusCode = null;
            long ttfb = 0;
            Dictionary<string, string> headersDict = [];

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                stopwatch.Restart();
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, target.Url);
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stoppingToken);

                    stopwatch.Stop();
                    ttfb = stopwatch.ElapsedMilliseconds;
                    statusCode = (int)response.StatusCode;

                    if (response.IsSuccessStatusCode)
                    {
                        status = UptimeStatus.Healthy;
                        errorMessage = null;

                        headersDict.Clear();
                        foreach (var header in response.Headers)
                        {
                            headersDict.TryAdd(header.Key, string.Join(", ", header.Value));
                        }

                        await using var stream = await response.Content.ReadAsStreamAsync(stoppingToken);
                        var buffer = new byte[8192];
                        _ = await stream.ReadAsync(buffer, stoppingToken);

                        break;
                    }

                    errorMessage = $"HTTP {(int)response.StatusCode}";
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    ttfb = stopwatch.ElapsedMilliseconds;
                    errorMessage = ex.Message;
                }

                if (attempt < 3 && status != UptimeStatus.Healthy)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }

            var latency = (int)ttfb;

            long tcpHandshake = 50;
            long tlsNegotiation = 50;
            long initLag = ttfb - (tcpHandshake + tlsNegotiation);
            bool isColdStart = initLag > 800;

            var uptimeLog = new UptimeLog
            {
                MonitorId = target.Id,
                StatusCode = statusCode,
                LatencyMs = latency,
                IsColdStart = isColdStart,
                ErrorMessage = errorMessage
            };

            dbContext.UptimeLogs.Add(uptimeLog);

            if (status == UptimeStatus.Healthy)
            {
                var securityGrade = CalculateSecurityGrade(headersDict);
                var rawHeadersJson = JsonSerializer.Serialize(headersDict, AppJsonSerializerContext.Default.DictionaryStringString);

                var securityAudit = new SecurityAudit
                {
                    MonitorId = target.Id,
                    HasCsp = headersDict.ContainsKey("Content-Security-Policy"),
                    HasHsts = headersDict.ContainsKey("Strict-Transport-Security"),
                    HasXfo = headersDict.ContainsKey("X-Frame-Options"),
                    HasNosniff = headersDict.ContainsKey("X-Content-Type-Options"),
                    RawHeaders = rawHeadersJson
                };

                dbContext.SecurityAudits.Add(securityAudit);

                if (target.CurrentSecurityGrade != 'U' && securityGrade > target.CurrentSecurityGrade)
                {
                    await alertManager.ProcessSecurityAlertAsync(target, securityGrade, stoppingToken);
                }

                target.CurrentSecurityGrade = securityGrade;
            }

            bool uptimeStateChanged = target.CurrentUptimeStatus != status;

            target.CurrentUptimeStatus = status;
            target.UpdatedAt = DateTime.UtcNow;

            dbContext.MonitorTargets.Update(target);

            if (uptimeStateChanged)
            {
                await alertManager.ProcessUptimeAlertAsync(target, status, stoppingToken);
            }

            var update = new PulseUpdate(target.Id, status, latency);
            await hubContext.Clients.Group(target.UserId.ToString()).SendAsync("ReceivePulse", update, stoppingToken);
        }

        await dbContext.SaveChangesAsync(stoppingToken);
    }

    private static char CalculateSecurityGrade(Dictionary<string, string> headers)
    {
        int score = 0;

        if (headers.ContainsKey("Content-Security-Policy")) score++;
        if (headers.ContainsKey("Strict-Transport-Security")) score++;
        if (headers.ContainsKey("X-Frame-Options")) score++;
        if (headers.ContainsKey("X-Content-Type-Options")) score++;

        return score switch
        {
            4 => 'A',
            3 => 'B',
            2 => 'C',
            1 => 'D',
            _ => 'F'
        };
    }
}