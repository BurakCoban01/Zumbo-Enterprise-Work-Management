using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Recurrences;

internal static class DeleteTemplateEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/templates/{templateId}", async (
            string templateId,
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            await service.ArchiveTemplateAsync(templateId, CorrelationId(http), ct);
            return Results.NoContent();
        }).WithZumboPermission(PermissionCatalog.WorkItemUpdate);
    }
}
