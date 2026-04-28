using System.Security.Claims;
using System.Text.Json.Serialization;
using KindleKeep.Api.Core.DTOs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Npgsql;
using NpgsqlTypes;

namespace KindleKeep.Api.Core.DTOs
{
    public record UpdateSettingsRequest(
        [property: JsonPropertyName("discordWebhookUrl")] string? DiscordWebhookUrl,
        [property: JsonPropertyName("enableEmailNotifications")] bool EnableEmailNotifications
    );

    public record UserUsageResponse(
        [property: JsonPropertyName("currentMonitors")] int CurrentMonitors,
        [property: JsonPropertyName("monitorLimit")] int MonitorLimit
    );

    public record UserSettingsResponse(
        [property: JsonPropertyName("discordWebhookUrl")] string? DiscordWebhookUrl,
        [property: JsonPropertyName("enableEmailNotifications")] bool EnableEmailNotifications
    );
}

namespace KindleKeep.Api.API.Endpoints
{
    public static class UserEndpoints
    {
        public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
        {
            var group = endpoints.MapGroup("/api/users").RequireAuthorization();

            group.MapGet("/usage", async ([FromServices] NpgsqlDataSource dataSource, HttpContext context) =>
            {
                var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                    ?? context.User.FindFirst("sub")?.Value;

                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                {
                    return Results.Unauthorized();
                }

                await using var connection = await dataSource.OpenConnectionAsync();
                await using var command = connection.CreateCommand();
                
                command.CommandText = @"
                    SELECT 
                        (SELECT COUNT(*) FROM ""MonitorTargets"" WHERE ""UserId"" = $1) as CurrentMonitors,
                        ""MonitorLimit""
                    FROM ""Users"" 
                    WHERE ""Id"" = $1;";
                
                command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = userId });

                await using var reader = await command.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    return Results.NotFound("User not found.");
                }

                var response = new UserUsageResponse(
                    reader.GetInt32(0),
                    reader.GetInt32(1)
                );

                return Results.Ok(response);
            });

            group.MapGet("/settings", async ([FromServices] NpgsqlDataSource dataSource, HttpContext context) =>
            {
                var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                    ?? context.User.FindFirst("sub")?.Value;

                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                {
                    return Results.Unauthorized();
                }

                await using var connection = await dataSource.OpenConnectionAsync();
                await using var command = connection.CreateCommand();
                
                command.CommandText = @"
                    SELECT ""DiscordWebhookUrl"", ""EnableEmailNotifications"" 
                    FROM ""Users"" 
                    WHERE ""Id"" = $1;";
                
                command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = userId });

                await using var reader = await command.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    return Results.NotFound("User not found.");
                }

                var response = new UserSettingsResponse(
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.GetBoolean(1)
                );

                return Results.Ok(response);
            });

            group.MapPut("/settings", async (UpdateSettingsRequest request, [FromServices] NpgsqlDataSource dataSource, HttpContext context) =>
            {
                var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                    ?? context.User.FindFirst("sub")?.Value;

                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                {
                    return Results.Unauthorized();
                }

                await using var connection = await dataSource.OpenConnectionAsync();
                await using var command = connection.CreateCommand();
                
                command.CommandText = @"
                    UPDATE ""Users"" 
                    SET ""DiscordWebhookUrl"" = $1, 
                        ""EnableEmailNotifications"" = $2 
                    WHERE ""Id"" = $3
                    RETURNING ""Id"";";

                command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = request.DiscordWebhookUrl ?? (object)DBNull.Value });
                command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Boolean, Value = request.EnableEmailNotifications });
                command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = userId });

                var result = await command.ExecuteScalarAsync();

                if (result == null)
                {
                    return Results.NotFound("User not found.");
                }

                return Results.NoContent();
            });

            return endpoints;
        }
    }
}