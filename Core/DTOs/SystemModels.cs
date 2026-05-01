using System;
using System.Text.Json.Serialization;

namespace KindleKeep.Api.Core.DTOs;

public record StayAwakeResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("timestamp")] DateTime Timestamp
);