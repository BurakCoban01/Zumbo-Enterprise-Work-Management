using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Recurrences;
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
            ArchiveWorkItemTemplateHandler handler,
            HttpContext http,
            CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new ArchiveWorkItemTemplateCommand(templateId, CorrelationId(http)),
                ct);
            return Results.NoContent();
        }).WithZumboPermission(PermissionCatalog.WorkItemUpdate);
    }
}
