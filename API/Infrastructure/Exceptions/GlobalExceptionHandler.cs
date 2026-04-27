using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace KindleKeep.Api.Infrastructure.Exceptions;

public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "An unhandled exception occurred.");

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Internal Server Error",
            Detail = exception.Message,
            Instance = httpContext.Request.Path
        };

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/problem+json";

        // Serializing directly to the HTTP response stream bypasses wrapper ambiguities and guarantees AOT compliance.
        var typeInfo = (JsonTypeInfo<ProblemDetails>)AppJsonSerializerContext.Default.GetTypeInfo(typeof(ProblemDetails))!;

        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            problemDetails,
            typeInfo,
            cancellationToken);

        return true;
    }
}