using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapDeleteRecurrencesByRecurrenceId(RouteGroupBuilder group){group.MapDelete("/recurrences/{recurrenceId}", async (
            string recurrenceId,
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            await service.ArchiveRecurrenceAsync(recurrenceId, CorrelationId(http), ct);
            return Results.NoContent();
        }).WithZumboPermission(PermissionCatalog.WorkItemUpdate);
}}
