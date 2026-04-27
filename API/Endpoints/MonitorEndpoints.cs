using System.Security.Claims;
using KindleKeep.Api.Core.DTOs;
using KindleKeep.Api.Core.Entities;
using KindleKeep.Api.Core.Enums;
using KindleKeep.Api.Infrastructure.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Npgsql;

namespace KindleKeep.Api.API.Endpoints;

public static class MonitorEndpoints
{
    public static IEndpointRouteBuilder MapMonitorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/monitors").RequireAuthorization();

        // Complex logic: [FromServices] explicitly flags the data source as a DI dependency 
        // to bypass the JSON deserializer in the Native AOT pipeline.
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
            command.Parameters.Add(new NpgsqlParameter { Value = userId });

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

        group.MapPost("/", async (CreateMonitorRequest request, KindleDbContext dbContext, HttpContext context) =>
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

            var monitor = new MonitorTarget
            {
                UserId = userId,
                Url = request.Url,
                FriendlyName = request.FriendlyName,
                CurrentUptimeStatus = UptimeStatus.Healthy,
                CurrentSecurityGrade = 'U',
                IsActive = true
            };

            dbContext.MonitorTargets.Add(monitor);
            await dbContext.SaveChangesAsync();

            var response = new MonitorResponse(
                monitor.Id,
                monitor.Url,
                monitor.FriendlyName,
                monitor.CurrentUptimeStatus,
                monitor.CurrentSecurityGrade,
                monitor.IsActive
            );

            return Results.Created($"/api/monitors/{monitor.Id}", response);
        });

        group.MapDelete("/{id:guid}", async (Guid id, KindleDbContext dbContext, [FromServices] NpgsqlDataSource dataSource, HttpContext context) =>
        {
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                ?? context.User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Results.Unauthorized();
            }

            await using var connection = await dataSource.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT \"UserId\" FROM \"MonitorTargets\" WHERE \"Id\" = $1";
            command.Parameters.Add(new NpgsqlParameter { Value = id });
            
            var ownerIdObj = await command.ExecuteScalarAsync();
            if (ownerIdObj == null)
            {
                return Results.NotFound();
            }
            
            var ownerId = (Guid)ownerIdObj;
            if (ownerId != userId)
            {
                return Results.Forbid();
            }

            var monitor = new MonitorTarget 
            { 
                Id = id, 
                UserId = ownerId,
                Url = string.Empty,
                FriendlyName = string.Empty
            };
            
            dbContext.MonitorTargets.Remove(monitor);
            await dbContext.SaveChangesAsync();

            return Results.NoContent();
        });

        return endpoints;
    }
}