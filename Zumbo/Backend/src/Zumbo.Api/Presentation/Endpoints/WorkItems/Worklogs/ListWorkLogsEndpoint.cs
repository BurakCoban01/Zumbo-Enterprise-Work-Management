using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Worklogs;

internal static class ListWorkLogsEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id}/worklogs", async (
            string id,
            int? page,
            int? pageSize,
            WorkItemActivityQueryService service,
            HttpContext http,
            CancellationToken ct) =>
                Ok(await service.ListWorkLogsAsync(id, page ?? 1, pageSize ?? 50, ct), http));
    }
}
