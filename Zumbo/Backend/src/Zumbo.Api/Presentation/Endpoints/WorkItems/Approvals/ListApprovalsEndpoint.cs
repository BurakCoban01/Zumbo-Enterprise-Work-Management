using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Approvals;

internal static class ListApprovalsEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id}/approvals", async (string id, int? page, int? pageSize, WorkItemActivityQueryService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ListApprovalsAsync(id, page ?? 1, pageSize ?? 50, ct), http));
    }
}
