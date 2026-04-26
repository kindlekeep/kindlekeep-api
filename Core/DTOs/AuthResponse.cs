namespace KindleKeep.Api.Core.DTOs;

public record AuthResponse(
    string Token,
    string DisplayName,
    string AvatarUrl
);