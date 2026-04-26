namespace KindleKeep.Api.Core.Entities;

public record SecurityAudit
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid MonitorId { get; init; }
    public DateTime? SslExpiryAt { get; init; }
    public string? SslIssuer { get; init; }
    
    // Core HTTP Defense Headers
    public bool HasCsp { get; init; }
    public bool HasHsts { get; init; }
    public bool HasXfo { get; init; }
    public bool HasNosniff { get; init; }
    
    public string? TlsVersion { get; init; }
    
    // JSONB payload for deep inspection later
    public string? RawHeaders { get; init; } 
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    // Navigation property
    public MonitorTarget? Monitor { get; init; }
}