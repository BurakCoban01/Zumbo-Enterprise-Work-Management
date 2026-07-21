using System.Net;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

public sealed class ApiExceptionMiddleware(
    RequestDelegate next,
    ILogger<ApiExceptionMiddleware> logger,
    IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Correlation-Id"] = context.TraceIdentifier;

        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var (statusCode, code, message) = MapException(ex, environment.IsDevelopment());
            logger.LogError(ex, "Request failed with {Code}. CorrelationId: {CorrelationId}", code, context.TraceIdentifier);
            context.Response.StatusCode = (int)statusCode;
            await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(code, message, context.TraceIdentifier));
        }
    }

    private static (HttpStatusCode StatusCode, string Code, string Message) MapException(
        Exception exception,
        bool includeDetails) =>
        exception switch
        {
            ValidationException ex => (HttpStatusCode.BadRequest, ex.Code, ex.Message),
            UnauthorizedException ex => (HttpStatusCode.Unauthorized, ex.Code, ex.Message),
            AuthenticationChallengeException ex => (HttpStatusCode.Unauthorized, ex.Code, ex.Message),
            ForbiddenException ex => (HttpStatusCode.Forbidden, ex.Code, ex.Message),
            NotFoundException ex => (HttpStatusCode.NotFound, ex.Code, ex.Message),
            ConflictException ex => (HttpStatusCode.Conflict, ex.Code, ex.Message),
            DocumentConcurrencyException ex => (HttpStatusCode.Conflict, "CONCURRENCY_CONFLICT", ex.Message),
            DocumentConflictException ex => (HttpStatusCode.Conflict, "DOCUMENT_CONFLICT", ex.Message),
            DocumentQueryException ex => (HttpStatusCode.BadRequest, "DOCUMENT_QUERY_INVALID", ex.Message),
            ZumboException ex => (HttpStatusCode.BadRequest, ex.Code, ex.Message),
            _ => (HttpStatusCode.InternalServerError, "UNEXPECTED_ERROR", includeDetails ? exception.Message : "Unexpected server error.")
        };
}
