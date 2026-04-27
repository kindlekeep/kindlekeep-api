using System.Security.Claims;
using KindleKeep.Api.Core.DTOs;
using KindleKeep.Api.Infrastructure.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace KindleKeep.Api.API.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/users").RequireAuthorization();

        group.MapGet("/me", async (KindleDbContext dbContext, HttpContext context) =>
        {
            // Complex logic: Standardizes the extraction of the subject identifier across different JWT implementations.
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                ?? context.User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userIdClaim))
            {
                return Results.Unauthorized();
            }

            if (!Guid.TryParse(userIdClaim, out var userId))
            {
                return Results.BadRequest("Invalid user identifier format.");
            }

            var user = await dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return Results.NotFound("User profile not found.");
            }

            var response = new UserProfileResponse(
                user.DisplayName,
                user.Email,
                user.AvatarUrl
            );

            return Results.Ok(response);
        });

        return endpoints;
    }
}