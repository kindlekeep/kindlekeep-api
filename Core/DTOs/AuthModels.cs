using System.Text.Json.Serialization;

namespace KindleKeep.Api.Core.DTOs;

public record GoogleTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("id_token")] string IdToken
);

public record GoogleUserResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("picture")] string AvatarUrl
);

public record GitlabTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken
);

public record GitlabUserResponse(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("avatar_url")] string AvatarUrl
);