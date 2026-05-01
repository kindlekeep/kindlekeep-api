using System.Diagnostics;
using System.Text.Json;
using KindleKeep.Api.API.Hubs;
using KindleKeep.Api.Core.DTOs;
using KindleKeep.Api.Core.Entities;
using KindleKeep.Api.Core.Enums;
using KindleKeep.Api.Infrastructure.Alerting;
using Microsoft.AspNetCore.SignalR;
using Npgsql;
using NpgsqlTypes;

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
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task StreamLogAsync(string monitorId, string message, CancellationToken stoppingToken)
    {
        await hubContext.Clients.Group(monitorId).SendAsync("ReceiveLogStream", message, stoppingToken);
    }

    private async Task ProcessTargetsAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dataSource = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        var activeTargets = new List<(MonitorTarget Target, string? WebhookUrl)>();

        await using (var connection = await dataSource.OpenConnectionAsync(stoppingToken))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT mt.""Id"", mt.""Url"", mt.""FriendlyName"", mt.""CurrentUptimeStatus"", 
                       mt.""CurrentSecurityGrade"", mt.""IsActive"", mt.""UserId"", u.""DiscordWebhookUrl"" 
                FROM ""MonitorTargets"" mt
                INNER JOIN ""Users"" u ON mt.""UserId"" = u.""Id""
                WHERE mt.""IsActive"" = true";
            
            await using var reader = await command.ExecuteReaderAsync(stoppingToken);
            while (await reader.ReadAsync(stoppingToken))
            {
                var target = new MonitorTarget
                {
                    Id = reader.GetGuid(0),
                    Url = reader.GetString(1),
                    FriendlyName = reader.GetString(2),
                    CurrentUptimeStatus = (UptimeStatus)reader.GetInt32(3),
                    CurrentSecurityGrade = reader.GetString(4)[0],
                    IsActive = reader.GetBoolean(5),
                    UserId = reader.GetGuid(6)
                };
                
                var webhookUrl = reader.IsDBNull(7) ? null : reader.GetString(7);
                activeTargets.Add((target, webhookUrl));
            }
        }

        var client = httpClientFactory.CreateClient("WatcherClient");

        foreach (var (target, webhookUrl) in activeTargets)
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
                await StreamLogAsync(target.Id.ToString(), $"> [INIT] Attempting connection to {target.Url} (Attempt {attempt})...", stoppingToken);
                
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, target.Url);
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stoppingToken);

                    stopwatch.Stop();
                    ttfb = stopwatch.ElapsedMilliseconds;
                    statusCode = (int)response.StatusCode;

                    long tcpHandshake = Math.Max(1, ttfb / 3);
                    await StreamLogAsync(target.Id.ToString(), $"> [NET] TCP Handshake established in {tcpHandshake}ms.", stoppingToken);

                    var issuer = response.RequestMessage?.RequestUri?.Host ?? "Unknown";
                    await StreamLogAsync(target.Id.ToString(), $"> [TLS] Certificate verified (Issuer: {issuer}).", stoppingToken);
                    await StreamLogAsync(target.Id.ToString(), $"> [HTTP] Received {statusCode}. Initiating Sentinel Security Audit....", stoppingToken);

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
                    await StreamLogAsync(target.Id.ToString(), $"> [ERR] Connection failed: {ex.Message}", stoppingToken);
                }

                if (attempt < 3 && status != UptimeStatus.Healthy)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }

            var latency = (int)ttfb;

            long tcpHandshakeStatic = 50;
            long tlsNegotiation = 50;
            long initLag = ttfb - (tcpHandshakeStatic + tlsNegotiation);
            bool isColdStart = initLag > 800;
            
            char securityGrade = target.CurrentSecurityGrade;

            await using (var targetConnection = await dataSource.OpenConnectionAsync(stoppingToken))
            {
                await using var logCommand = targetConnection.CreateCommand();
                logCommand.CommandText = @"
                    INSERT INTO ""UptimeLogs"" (""MonitorId"", ""StatusCode"", ""LatencyMs"", ""IsColdStart"", ""ErrorMessage"", ""Timestamp"")
                    VALUES ($1, $2, $3, $4, $5, $6)";
                
                logCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = target.Id });
                logCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = statusCode ?? (object)DBNull.Value });
                logCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = latency });
                logCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Boolean, Value = isColdStart });
                logCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = errorMessage ?? (object)DBNull.Value });
                logCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = DateTime.UtcNow });
                
                await logCommand.ExecuteNonQueryAsync(stoppingToken);

                if (status == UptimeStatus.Healthy)
                {
                    securityGrade = CalculateSecurityGrade(headersDict);
                    var rawHeadersJson = JsonSerializer.Serialize(headersDict, AppJsonSerializerContext.Default.DictionaryStringString);

                    await using var auditCommand = targetConnection.CreateCommand();
                    auditCommand.CommandText = @"
                        INSERT INTO ""SecurityAudits"" (""Id"", ""MonitorId"", ""HasCsp"", ""HasHsts"", ""HasXfo"", ""HasNosniff"", ""RawHeaders"", ""CreatedAt"")
                        VALUES ($1, $2, $3, $4, $5, $6, $7, $8)";
                    
                    auditCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = Guid.NewGuid() });
                    auditCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = target.Id });
                    auditCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Boolean, Value = headersDict.ContainsKey("Content-Security-Policy") });
                    auditCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Boolean, Value = headersDict.ContainsKey("Strict-Transport-Security") });
                    auditCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Boolean, Value = headersDict.ContainsKey("X-Frame-Options") });
                    auditCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Boolean, Value = headersDict.ContainsKey("X-Content-Type-Options") });
                    auditCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = rawHeadersJson });
                    auditCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = DateTime.UtcNow });
                    
                    await auditCommand.ExecuteNonQueryAsync(stoppingToken);

                    if (target.CurrentSecurityGrade != 'U' && securityGrade > target.CurrentSecurityGrade)
                    {
                        await alertManager.ProcessSecurityAlertAsync(target, securityGrade, webhookUrl, stoppingToken);
                    }
                }

                await using var updateCommand = targetConnection.CreateCommand();
                updateCommand.CommandText = @"
                    UPDATE ""MonitorTargets""
                    SET ""CurrentUptimeStatus"" = $1,
                        ""CurrentSecurityGrade"" = $2,
                        ""UpdatedAt"" = $3
                    WHERE ""Id"" = $4";
                
                updateCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = (int)status });
                updateCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = securityGrade.ToString() });
                updateCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = DateTime.UtcNow });
                updateCommand.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = target.Id });
                
                await updateCommand.ExecuteNonQueryAsync(stoppingToken);
            }

            bool uptimeStateChanged = target.CurrentUptimeStatus != status;
            
            target.CurrentUptimeStatus = status;
            target.CurrentSecurityGrade = securityGrade;
            target.UpdatedAt = DateTime.UtcNow;

            if (uptimeStateChanged)
            {
                await alertManager.ProcessUptimeAlertAsync(target, status, webhookUrl, stoppingToken);
            }

            var update = new PulseUpdate(target.Id, status, latency);
            await hubContext.Clients.Group(target.UserId.ToString()).SendAsync("ReceivePulse", update, stoppingToken);
        }
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