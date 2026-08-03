using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapDeleteTemplatesByTemplateId(RouteGroupBuilder group){group.MapDelete("/templates/{templateId}", async (
            string templateId,
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            await service.ArchiveTemplateAsync(templateId, CorrelationId(http), ct);
            return Results.NoContent();
        }).WithZumboPermission(PermissionCatalog.WorkItemUpdate);
}}
