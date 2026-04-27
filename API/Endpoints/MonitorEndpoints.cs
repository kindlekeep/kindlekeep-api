using System.Security.Claims;
using KindleKeep.Api.Core.DTOs;
using KindleKeep.Api.Core.Enums;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Npgsql;
using NpgsqlTypes;

namespace KindleKeep.Api.API.Endpoints;

public static class MonitorEndpoints
{
    public static IEndpointRouteBuilder MapMonitorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/monitors").RequireAuthorization();

        group.MapGet("/", async ([FromServices] NpgsqlDataSource dataSource, HttpContext context) =>
        {
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                ?? context.User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Results.Unauthorized();
            }

            var monitors = new List<MonitorResponse>();
            
            await using var connection = await dataSource.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT \"Id\", \"Url\", \"FriendlyName\", \"CurrentUptimeStatus\", \"CurrentSecurityGrade\", \"IsActive\" FROM \"MonitorTargets\" WHERE \"UserId\" = $1";
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = userId });

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                monitors.Add(new MonitorResponse(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    (UptimeStatus)reader.GetInt32(3),
                    reader.GetString(4)[0],
                    reader.GetBoolean(5)
                ));
            }

            return Results.Ok(monitors);
        });

        group.MapPost("/", async (CreateMonitorRequest request, [FromServices] NpgsqlDataSource dataSource, HttpContext context) =>
        {
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                ?? context.User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(request.FriendlyName))
            {
                return Results.BadRequest("Friendly Name cannot be empty.");
            }

            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uriResult) || 
                (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
            {
                return Results.BadRequest("Invalid URL format. Must be an absolute HTTP or HTTPS URI.");
            }

            var monitorId = Guid.NewGuid();

            await using var connection = await dataSource.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            
            command.CommandText = @"
                INSERT INTO ""MonitorTargets"" (""Id"", ""UserId"", ""Url"", ""FriendlyName"", ""IntervalMinutes"", ""RequestTimeout"", ""IsActive"", ""CurrentUptimeStatus"", ""CurrentSecurityGrade"", ""UpdatedAt"")
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
                RETURNING ""Id"";";

            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = monitorId });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = userId });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = request.Url });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = request.FriendlyName });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = 10 });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = 30 });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Boolean, Value = true });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = 0 });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = "U" });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = DateTime.UtcNow });

            await command.ExecuteScalarAsync();

            var response = new MonitorResponse(
                monitorId,
                request.Url,
                request.FriendlyName,
                (UptimeStatus)0,
                'U',
                true
            );

            return Results.Created($"/api/monitors/{monitorId}", response);
        });

        group.MapDelete("/{id:guid}", async (Guid id, [FromServices] NpgsqlDataSource dataSource, HttpContext context) =>
        {
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                ?? context.User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Results.Unauthorized();
            }

            // Complex logic: Combines authorization and deletion into a single atomic database operation.
            await using var connection = await dataSource.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM \"MonitorTargets\" WHERE \"Id\" = $1 AND \"UserId\" = $2";
            
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = id });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = userId });

            var rowsAffected = await command.ExecuteNonQueryAsync();

            if (rowsAffected == 0)
            {
                return Results.NotFound();
            }

            return Results.NoContent();
        });

        return endpoints;
    }
}