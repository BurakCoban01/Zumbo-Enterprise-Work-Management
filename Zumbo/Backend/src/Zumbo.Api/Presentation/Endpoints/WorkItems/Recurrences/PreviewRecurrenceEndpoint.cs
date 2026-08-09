using Zumbo.Modules.WorkItems;
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
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.PreviewRecurrenceAsync(request, ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemCreate);
    }
}
