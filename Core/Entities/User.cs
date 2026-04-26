using KindleKeep.Api.Core.Enums;

namespace KindleKeep.Api.Core.Entities;

public record User
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string ExternalId { get; init; }
    public required AuthProvider AuthProvider { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; set; }
    public required string AvatarUrl { get; set; }
    public int MonitorLimit { get; init; } = 5;

    // Navigation property for Entity Framework relations
    public ICollection<MonitorTarget> Monitors { get; init; } = [];
}