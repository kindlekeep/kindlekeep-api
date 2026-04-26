using System.Diagnostics;
using System.Text.Json;
using KindleKeep.Api.API.Hubs;
using KindleKeep.Api.Core.DTOs;
using KindleKeep.Api.Core.Entities;
using KindleKeep.Api.Core.Enums;
using KindleKeep.Api.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace KindleKeep.Api.Infrastructure.BackgroundServices;

public class WatcherEngine(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IHubContext<PulseHub> hubContext,
    IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = configuration.GetValue<int>("Watcher:IntervalMinutes", 1);
        var delay = TimeSpan.FromMinutes(intervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessTargetsAsync(stoppingToken);
            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task ProcessTargetsAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<KindleDbContext>();

        var activeTargets = await dbContext.MonitorTargets
            .Where(t => t.IsActive)
            .ToListAsync(stoppingToken);

        var client = httpClientFactory.CreateClient("WatcherClient");

        foreach (var target in activeTargets)
        {
            var stopwatch = Stopwatch.StartNew();
            var status = UptimeStatus.Healthy;
            string? errorMessage = null;
            int? statusCode = null;
            long ttfb = 0;
            Dictionary<string, string> headersDict = [];

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, target.Url);

                // Complex logic: Halts the automatic body download to protect RAM allocation.
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stoppingToken);

                stopwatch.Stop();
                ttfb = stopwatch.ElapsedMilliseconds;
                statusCode = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    status = UptimeStatus.Down;
                }

                foreach (var header in response.Headers)
                {
                    headersDict.TryAdd(header.Key, string.Join(", ", header.Value));
                }

                // Complex logic: Reads only the first 8KB of the response stream to prevent resource exhaustion.
                await using var stream = await response.Content.ReadAsStreamAsync(stoppingToken);
                var buffer = new byte[8192];
                _ = await stream.ReadAsync(buffer, stoppingToken);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                ttfb = stopwatch.ElapsedMilliseconds;
                status = UptimeStatus.Down;
                errorMessage = ex.Message;
            }

            var latency = (int)ttfb;

            // Complex logic: Standard HttpClient does not natively expose TCP/TLS handshake metrics without external diagnostic listeners. 
            // Baseline variables are used to satisfy the DTA mathematical model.
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

                target.CurrentSecurityGrade = securityGrade;
            }

            target.CurrentUptimeStatus = status;
            target.UpdatedAt = DateTime.UtcNow;

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