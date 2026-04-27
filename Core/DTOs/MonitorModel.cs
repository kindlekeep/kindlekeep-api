using System.Text.Json.Serialization;
using KindleKeep.Api.Core.Enums;

namespace KindleKeep.Api.Core.DTOs;

public record CreateMonitorRequest(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("friendlyName")] string FriendlyName
);

public record MonitorResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("friendlyName")] string FriendlyName,
    [property: JsonPropertyName("currentUptimeStatus")] UptimeStatus CurrentUptimeStatus,
    [property: JsonPropertyName("currentSecurityGrade")] char CurrentSecurityGrade,
    [property: JsonPropertyName("isActive")] bool IsActive
);