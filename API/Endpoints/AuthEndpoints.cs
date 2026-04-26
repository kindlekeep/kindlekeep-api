using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using KindleKeep.Api.Core.DTOs;
using KindleKeep.Api.Core.Entities;
using KindleKeep.Api.Core.Enums;
using KindleKeep.Api.Infrastructure.Data;
using KindleKeep.Api.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace KindleKeep.Api.API.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth");

        group.MapGet("/login", (IConfiguration configuration, HttpContext context) =>
        {
            var clientId = configuration["Authentication:GitHub:ClientId"] 
                ?? throw new InvalidOperationException("GitHub ClientId is missing.");

            var request = context.Request;
            var baseUrl = $"{request.Scheme}://{request.Host}";
            var redirectUri = $"{baseUrl}/api/auth/callback/github";

            var state = "stateless-csrf-placeholder";

            var queryParams = new Dictionary<string, string?>
            {
                { "client_id", clientId },
                { "redirect_uri", redirectUri },
                { "scope", "read:user user:email" },
                { "state", state }
            };

            var authorizationUrl = QueryHelpers.AddQueryString("https://github.com/login/oauth/authorize", queryParams);

            return TypedResults.Redirect(authorizationUrl);
        })
        .WithName("GitHubLogin");

        group.MapGet("/callback/github", async (
            [FromQuery] string code,
            [FromQuery] string state,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            KindleDbContext dbContext,
            TokenService tokenService,
            HttpContext context) =>
        {
            var clientId = configuration["Authentication:GitHub:ClientId"] 
                ?? throw new InvalidOperationException("GitHub ClientId is missing.");
            var clientSecret = configuration["Authentication:GitHub:ClientSecret"] 
                ?? throw new InvalidOperationException("GitHub ClientSecret is missing.");

            var request = context.Request;
            var baseUrl = $"{request.Scheme}://{request.Host}";
            var redirectUri = $"{baseUrl}/api/auth/callback/github";

            var client = httpClientFactory.CreateClient("GitHub");

            var tokenPayload = new Dictionary<string, string>
            {
                { "client_id", clientId },
                { "client_secret", clientSecret },
                { "code", code },
                { "redirect_uri", redirectUri }
            };

            var tokenResponseMsg = await client.PostAsync("https://github.com/login/oauth/access_token", new FormUrlEncodedContent(tokenPayload));
            tokenResponseMsg.EnsureSuccessStatusCode();

            // Native AOT optimization: pass the generated JSON context
            var tokenResult = await tokenResponseMsg.Content.ReadFromJsonAsync(AppJsonSerializerContext.Default.GithubTokenResponse);
            if (tokenResult == null || string.IsNullOrEmpty(tokenResult.AccessToken))
            {
                return Results.BadRequest("Failed to retrieve access token.");
            }

            var userRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            userRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenResult.AccessToken);
            
            var userResponseMsg = await client.SendAsync(userRequest);
            userResponseMsg.EnsureSuccessStatusCode();

            var githubUser = await userResponseMsg.Content.ReadFromJsonAsync(AppJsonSerializerContext.Default.GithubUserResponse);
            if (githubUser == null)
            {
                return Results.BadRequest("Failed to retrieve user profile.");
            }

            var externalId = githubUser.Id.ToString();
            var user = await dbContext.Users.FirstOrDefaultAsync(u => u.ExternalId == externalId);

            if (user == null)
            {
                user = new User
                {
                    ExternalId = externalId,
                    AuthProvider = AuthProvider.GitHub,
                    Email = githubUser.Email ?? $"{githubUser.Login}@users.noreply.github.com",
                    DisplayName = githubUser.Name ?? githubUser.Login,
                    AvatarUrl = githubUser.AvatarUrl
                };
                dbContext.Users.Add(user);
            }
            else
            {
                user.DisplayName = githubUser.Name ?? githubUser.Login;
                user.AvatarUrl = githubUser.AvatarUrl;
            }

            await dbContext.SaveChangesAsync();

            var jwtToken = tokenService.GenerateToken(user);
            var authResponse = new AuthResponse(jwtToken, user.DisplayName, user.AvatarUrl);

            return Results.Ok(authResponse);
        })
        .WithName("GitHubCallback");

        return endpoints;
    }
}