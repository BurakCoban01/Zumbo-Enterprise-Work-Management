using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.BulkOperations;

internal static class ListBulkJobsEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/bulk/jobs", async (
            string projectId, int? page, int? pageSize,
            WorkItemBulkJobService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ListAsync(projectId, page ?? 1, pageSize ?? 50, ct), http));
    }
}
