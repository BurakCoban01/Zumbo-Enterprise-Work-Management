using System.Diagnostics;
using System.Security.Claims;

public sealed class RequestTelemetryMiddleware(RequestDelegate next, ILogger<RequestTelemetryMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        await next(context);
        stopwatch.Stop();

        logger.LogInformation(
            "HTTP {Method} {RequestPath} completed with {StatusCode} in {ElapsedMs} ms for user {UserId} organization {OrganizationId} correlation {CorrelationId}",
            context.Request.Method,
            context.Request.Path.Value,
            context.Response.StatusCode,
            stopwatch.Elapsed.TotalMilliseconds,
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
            context.User.FindFirstValue("organizationId") ?? "none",
            context.TraceIdentifier);
    }
}
