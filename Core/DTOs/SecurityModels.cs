using System;
using System.Text.Json.Serialization;

namespace KindleKeep.Api.Core.DTOs;

public record VaultTargetResponse(
    [property: JsonPropertyName("monitorId")] Guid MonitorId,
    [property: JsonPropertyName("friendlyName")] string FriendlyName,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("securityGrade")] char SecurityGrade,
    [property: JsonPropertyName("lastAudit")] VaultAuditDetail? LastAudit
);

public record VaultAuditDetail(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("sslIssuer")] string? SslIssuer,
    [property: JsonPropertyName("sslExpiryAt")] DateTime? SslExpiryAt,
    [property: JsonPropertyName("hasCsp")] bool HasCsp,
    [property: JsonPropertyName("hasHsts")] bool HasHsts,
    [property: JsonPropertyName("hasXfo")] bool HasXfo,
    [property: JsonPropertyName("hasNosniff")] bool HasNosniff,
    [property: JsonPropertyName("rawHeaders")] string RawHeaders,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt
);