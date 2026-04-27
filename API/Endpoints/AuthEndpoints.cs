using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using KindleKeep.Api.Core.DTOs;
using KindleKeep.Api.Core.Entities;
using KindleKeep.Api.Core.Enums;
using KindleKeep.Api.Infrastructure.Identity;
using Npgsql;
using NpgsqlTypes;

namespace KindleKeep.Api.API.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth");

        group.MapGet("/login/github", (IConfiguration configuration, HttpContext context) =>
        {
            var clientId = configuration["Authentication:GitHub:ClientId"] 
                ?? throw new InvalidOperationException("GitHub ClientId is missing.");

            var redirectUri = $"{context.Request.Scheme}://{context.Request.Host}/api/auth/callback/github";
            var queryParams = new Dictionary<string, string?>
            {
                { "client_id", clientId },
                { "redirect_uri", redirectUri },
                { "scope", "read:user user:email" },
                { "state", "stateless-csrf-placeholder" }
            };

            return TypedResults.Redirect(QueryHelpers.AddQueryString("https://github.com/login/oauth/authorize", queryParams));
        });

        group.MapGet("/callback/github", async (
            [FromQuery] string code,
            [FromQuery] string state,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            [FromServices] NpgsqlDataSource dataSource,
            TokenService tokenService,
            HttpContext context) =>
        {
            return await ProcessOAuthCallbackAsync(
                code, AuthProvider.GitHub, "Authentication:GitHub",
                "https://github.com/login/oauth/access_token", "https://api.github.com/user",
                configuration, httpClientFactory, dataSource, tokenService, context);
        });

        group.MapGet("/login/google", (IConfiguration configuration, HttpContext context) =>
        {
            var clientId = configuration["Authentication:Google:ClientId"] 
                ?? throw new InvalidOperationException("Google ClientId is missing.");

            var redirectUri = $"{context.Request.Scheme}://{context.Request.Host}/api/auth/callback/google";
            var queryParams = new Dictionary<string, string?>
            {
                { "client_id", clientId },
                { "redirect_uri", redirectUri },
                { "response_type", "code" },
                { "scope", "openid email profile" },
                { "state", "stateless-csrf-placeholder" }
            };

            return TypedResults.Redirect(QueryHelpers.AddQueryString("https://accounts.google.com/o/oauth2/v2/auth", queryParams));
        });

        group.MapGet("/callback/google", async (
            [FromQuery] string code,
            [FromQuery] string state,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            [FromServices] NpgsqlDataSource dataSource,
            TokenService tokenService,
            HttpContext context) =>
        {
            return await ProcessOAuthCallbackAsync(
                code, AuthProvider.Google, "Authentication:Google",
                "https://oauth2.googleapis.com/token", "https://www.googleapis.com/oauth2/v2/userinfo",
                configuration, httpClientFactory, dataSource, tokenService, context);
        });

        group.MapGet("/login/gitlab", (IConfiguration configuration, HttpContext context) =>
        {
            var clientId = configuration["Authentication:GitLab:ClientId"] 
                ?? throw new InvalidOperationException("GitLab ClientId is missing.");

            var redirectUri = $"{context.Request.Scheme}://{context.Request.Host}/api/auth/callback/gitlab";
            var queryParams = new Dictionary<string, string?>
            {
                { "client_id", clientId },
                { "redirect_uri", redirectUri },
                { "response_type", "code" },
                { "scope", "read_user" },
                { "state", "stateless-csrf-placeholder" }
            };

            return TypedResults.Redirect(QueryHelpers.AddQueryString("https://gitlab.com/oauth/authorize", queryParams));
        });

        group.MapGet("/callback/gitlab", async (
            [FromQuery] string code,
            [FromQuery] string state,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            [FromServices] NpgsqlDataSource dataSource,
            TokenService tokenService,
            HttpContext context) =>
        {
            return await ProcessOAuthCallbackAsync(
                code, AuthProvider.GitLab, "Authentication:GitLab",
                "https://gitlab.com/oauth/token", "https://gitlab.com/api/v4/user",
                configuration, httpClientFactory, dataSource, tokenService, context);
        });

        return endpoints;
    }

    private static async Task<IResult> ProcessOAuthCallbackAsync(
        string code,
        AuthProvider provider,
        string configSection,
        string tokenUrl,
        string userUrl,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        NpgsqlDataSource dataSource,
        TokenService tokenService,
        HttpContext context)
    {
        var clientId = configuration[$"{configSection}:ClientId"] 
            ?? throw new InvalidOperationException($"{provider} ClientId is missing.");
        var clientSecret = configuration[$"{configSection}:ClientSecret"] 
            ?? throw new InvalidOperationException($"{provider} ClientSecret is missing.");

        var redirectUri = $"{context.Request.Scheme}://{context.Request.Host}/api/auth/callback/{provider.ToString().ToLower()}";
        var client = httpClientFactory.CreateClient(provider.ToString());

        var tokenPayload = new Dictionary<string, string>
        {
            { "client_id", clientId },
            { "client_secret", clientSecret },
            { "code", code },
            { "redirect_uri", redirectUri },
            { "grant_type", "authorization_code" }
        };

        var tokenResponseMsg = await client.PostAsync(tokenUrl, new FormUrlEncodedContent(tokenPayload));
        tokenResponseMsg.EnsureSuccessStatusCode();

        string accessToken;
        if (provider == AuthProvider.GitHub)
        {
            var result = await tokenResponseMsg.Content.ReadFromJsonAsync(AppJsonSerializerContext.Default.GithubTokenResponse);
            accessToken = result?.AccessToken ?? throw new InvalidOperationException("Failed to retrieve GitHub access token.");
        }
        else if (provider == AuthProvider.Google)
        {
            var result = await tokenResponseMsg.Content.ReadFromJsonAsync(AppJsonSerializerContext.Default.GoogleTokenResponse);
            accessToken = result?.AccessToken ?? throw new InvalidOperationException("Failed to retrieve Google access token.");
        }
        else
        {
            var result = await tokenResponseMsg.Content.ReadFromJsonAsync(AppJsonSerializerContext.Default.GitlabTokenResponse);
            accessToken = result?.AccessToken ?? throw new InvalidOperationException("Failed to retrieve GitLab access token.");
        }

        var userRequest = new HttpRequestMessage(HttpMethod.Get, userUrl);
        userRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        
        var userResponseMsg = await client.SendAsync(userRequest);
        userResponseMsg.EnsureSuccessStatusCode();

        string externalId, email, displayName, avatarUrl;

        if (provider == AuthProvider.GitHub)
        {
            var profile = await userResponseMsg.Content.ReadFromJsonAsync(AppJsonSerializerContext.Default.GithubUserResponse)
                ?? throw new InvalidOperationException("Failed to retrieve GitHub user profile.");
            externalId = profile.Id.ToString();
            email = profile.Email ?? $"{profile.Login}@users.noreply.github.com";
            displayName = profile.Name ?? profile.Login;
            avatarUrl = profile.AvatarUrl;
        }
        else if (provider == AuthProvider.Google)
        {
            var profile = await userResponseMsg.Content.ReadFromJsonAsync(AppJsonSerializerContext.Default.GoogleUserResponse)
                ?? throw new InvalidOperationException("Failed to retrieve Google user profile.");
            externalId = profile.Id;
            email = profile.Email ?? $"google_{profile.Id}@users.noreply.google.com";
            displayName = profile.Name ?? "Google User";
            avatarUrl = profile.AvatarUrl;
        }
        else
        {
            var profile = await userResponseMsg.Content.ReadFromJsonAsync(AppJsonSerializerContext.Default.GitlabUserResponse)
                ?? throw new InvalidOperationException("Failed to retrieve GitLab user profile.");
            externalId = profile.Id.ToString();
            email = profile.Email ?? $"{profile.Username}@users.noreply.gitlab.com";
            displayName = profile.Name ?? profile.Username;
            avatarUrl = profile.AvatarUrl;
        }

        var newUserId = Guid.NewGuid();
        Guid finalUserId;

        var defaultMonitorLimit = configuration.GetValue<int>("Users:DefaultMonitorLimit", 5);

        // Native AOT compliant PostgreSQL Upsert utilizing strictly typed parameters to bypass EF Core dynamic evaluation.
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO ""Users"" (""Id"", ""ExternalId"", ""AuthProvider"", ""Email"", ""DisplayName"", ""AvatarUrl"", ""MonitorLimit"")
            VALUES ($1, $2, $3, $4, $5, $6, $7)
            ON CONFLICT (""ExternalId"") 
            DO UPDATE SET 
                ""DisplayName"" = EXCLUDED.""DisplayName"",
                ""AvatarUrl"" = EXCLUDED.""AvatarUrl"",
                ""Email"" = EXCLUDED.""Email""
            RETURNING ""Id"";";

        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = newUserId });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = externalId });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = (int)provider });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = email });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = displayName });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = string.IsNullOrEmpty(avatarUrl) ? string.Empty : avatarUrl });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = defaultMonitorLimit });

        var returnedIdObj = await command.ExecuteScalarAsync();
        finalUserId = returnedIdObj != null ? (Guid)returnedIdObj : newUserId;

        var user = new User
        {
            Id = finalUserId,
            ExternalId = externalId,
            AuthProvider = provider,
            Email = email,
            DisplayName = displayName,
            AvatarUrl = string.IsNullOrEmpty(avatarUrl) ? string.Empty : avatarUrl
        };

        var jwtToken = tokenService.GenerateToken(user);
        var authResponse = new AuthResponse(jwtToken, user.DisplayName, user.AvatarUrl);

        return Results.Ok(authResponse);
    }
}