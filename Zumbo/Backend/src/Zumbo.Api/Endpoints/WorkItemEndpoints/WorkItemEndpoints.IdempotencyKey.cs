using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{

    private static string IdempotencyKey(HttpContext http) =>
        http.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty;
}
