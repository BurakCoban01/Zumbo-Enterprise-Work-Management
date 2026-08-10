namespace Zumbo.Api.Presentation.Endpoints.WorkItems.BulkOperations;

internal static class BulkOperationEndpointHeaders
{
    internal static string IdempotencyKey(HttpContext http) =>
        http.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty;
}
