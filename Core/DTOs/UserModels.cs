using System.Text.Json.Serialization;

namespace KindleKeep.Api.Core.DTOs;

public record UserProfileResponse(
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("avatarUrl")] string AvatarUrl
);