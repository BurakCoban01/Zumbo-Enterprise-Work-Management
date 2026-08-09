using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Recurrences;

internal static class SetRecurrenceStateEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/recurrences/{recurrenceId}/state", async (
            string recurrenceId,
            SetWorkItemRecurrenceStateRequest request,
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SetRecurrenceStateAsync(
                recurrenceId, request.Active, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
    }
}
