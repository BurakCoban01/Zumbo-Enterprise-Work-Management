using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.BulkOperations;

internal static class RetryBulkJobEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk/jobs/{jobId}/retry", async (
            string jobId, WorkItemBulkJobService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.RetryAsync(jobId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
    }
}
