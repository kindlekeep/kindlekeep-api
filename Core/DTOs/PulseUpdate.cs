using KindleKeep.Api.Core.Enums;

namespace KindleKeep.Api.Core.DTOs;

public record PulseUpdate(
    Guid MonitorId, 
    UptimeStatus NewStatus, 
    int LatencyMs
);