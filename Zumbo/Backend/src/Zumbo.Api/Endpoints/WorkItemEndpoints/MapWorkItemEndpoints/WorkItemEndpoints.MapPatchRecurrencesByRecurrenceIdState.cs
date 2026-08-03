using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPatchRecurrencesByRecurrenceIdState(RouteGroupBuilder group){group.MapPatch("/recurrences/{recurrenceId}/state", async (
            string recurrenceId,
            SetWorkItemRecurrenceStateRequest request,
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SetRecurrenceStateAsync(
                recurrenceId, request.Active, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
}}
