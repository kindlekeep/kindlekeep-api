namespace KindleKeep.Api.Core.Entities;

public record UptimeLog
{
    public long Id { get; init; }
    public required Guid MonitorId { get; init; }
    public int? StatusCode { get; init; }
    public required int LatencyMs { get; init; }
    public bool IsColdStart { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    // Navigation property
    public MonitorTarget? Monitor { get; init; }
}