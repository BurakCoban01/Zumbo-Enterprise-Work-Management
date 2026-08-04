using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

internal static class ApiEndpointResults
{
    internal static IResult Ok<T>(T data, HttpContext http)
    {
        ApplyVersionHeader(data, http);
        return Results.Ok(ApiResponse<T>.Ok(data, CorrelationId(http)));
    }

    internal static IResult Created<T>(T data, HttpContext http)
    {
        ApplyVersionHeader(data, http);
        return Results.Json(ApiResponse<T>.Ok(data, CorrelationId(http)), statusCode: StatusCodes.Status201Created);
    }

    internal static bool IsPreviewableContentType(string contentType) =>
        contentType.Equals("text/plain", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("image/gif", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase);

    internal static string CorrelationId(HttpContext http)
    {
        if (!http.Response.Headers.ContainsKey("X-Correlation-Id"))
        {
            http.Response.Headers["X-Correlation-Id"] = http.TraceIdentifier;
        }

        return http.TraceIdentifier;
    }

    private static void ApplyVersionHeader<T>(T data, HttpContext http)
    {
        if (data is IVersionedResource { Version: > 0 } versioned)
        {
            http.Response.Headers.ETag = $"\"{versioned.Version}\"";
        }
    }
}
