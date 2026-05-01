using System;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Npgsql;
using NpgsqlTypes;
using KindleKeep.Api.Core.DTOs;

namespace KindleKeep.Api.API.Endpoints
{
    public static class VaultEndpoints
    {
        public static IEndpointRouteBuilder MapVaultEndpoints(this IEndpointRouteBuilder endpoints)
        {
            var group = endpoints.MapGroup("/api/security/vault").RequireAuthorization();

            group.MapGet("/", async ([FromServices] NpgsqlDataSource dataSource, HttpContext context) =>
            {
                var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                    ?? context.User.FindFirst("sub")?.Value;

                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                {
                    return Results.Unauthorized();
                }

                var vaultItems = new List<VaultTargetResponse>();

                await using var connection = await dataSource.OpenConnectionAsync();
                await using var command = connection.CreateCommand();
                
                command.CommandText = @"
                    WITH LatestAudits AS (
                        SELECT DISTINCT ON (""MonitorId"") *
                        FROM ""SecurityAudits""
                        ORDER BY ""MonitorId"", ""CreatedAt"" DESC
                    )
                    SELECT 
                        mt.""Id"", 
                        mt.""FriendlyName"", 
                        mt.""Url"", 
                        mt.""CurrentSecurityGrade"",
                        la.""Id"" AS AuditId,
                        la.""SslIssuer"",
                        la.""SslExpiryAt"",
                        la.""HasCsp"",
                        la.""HasHsts"",
                        la.""HasXfo"",
                        la.""HasNosniff"",
                        la.""RawHeaders"",
                        la.""CreatedAt"" AS AuditCreatedAt
                    FROM ""MonitorTargets"" mt
                    LEFT JOIN LatestAudits la ON mt.""Id"" = la.""MonitorId""
                    WHERE mt.""UserId"" = $1
                    ORDER BY mt.""FriendlyName"" ASC;";

                command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = userId });

                await using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    VaultAuditDetail? auditDetail = null;
                    if (!reader.IsDBNull(4))
                    {
                        auditDetail = new VaultAuditDetail(
                            reader.GetGuid(4),
                            reader.IsDBNull(5) ? null : reader.GetString(5),
                            reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                            reader.GetBoolean(7),
                            reader.GetBoolean(8),
                            reader.GetBoolean(9),
                            reader.GetBoolean(10),
                            reader.GetString(11),
                            reader.GetDateTime(12)
                        );
                    }

                    vaultItems.Add(new VaultTargetResponse(
                        reader.GetGuid(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3)[0],
                        auditDetail
                    ));
                }

                return Results.Ok(vaultItems);
            });

            return endpoints;
        }
    }
}