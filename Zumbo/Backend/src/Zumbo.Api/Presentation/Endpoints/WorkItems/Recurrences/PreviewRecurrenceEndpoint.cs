using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Recurrences;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Recurrences;

internal static class PreviewRecurrenceEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/recurrences/preview", async (
            PreviewWorkItemRecurrenceRequest request,
            PreviewWorkItemRecurrenceHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(new PreviewWorkItemRecurrenceQuery(request), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemCreate);
    }
}
