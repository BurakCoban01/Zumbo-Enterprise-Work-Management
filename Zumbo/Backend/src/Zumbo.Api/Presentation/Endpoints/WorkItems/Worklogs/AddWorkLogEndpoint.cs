using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Worklogs;

internal static class AddWorkLogEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id}/worklogs", async (
            string id,
            AddWorkLogRequest request,
            AddWorkLogHandler handler,
            HttpContext http,
            CancellationToken ct) =>
                Ok(await handler.HandleAsync(new AddWorkLogCommand(id, request), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkLogCreate);
    }
}
