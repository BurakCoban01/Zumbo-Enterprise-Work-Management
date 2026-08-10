using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.BulkOperations;

internal static class GetBulkJobEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/bulk/jobs/{jobId}", async (
            string jobId, WorkItemBulkJobService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.GetAsync(jobId, ct), http));
    }
}
