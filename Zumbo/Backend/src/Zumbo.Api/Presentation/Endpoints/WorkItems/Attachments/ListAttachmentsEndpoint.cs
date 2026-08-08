using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Attachments;

internal static class ListAttachmentsEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id}/attachments", async (string id, int? page, int? pageSize, WorkItemActivityQueryService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ListAttachmentsAsync(id, page ?? 1, pageSize ?? 50, ct), http));
    }
}
