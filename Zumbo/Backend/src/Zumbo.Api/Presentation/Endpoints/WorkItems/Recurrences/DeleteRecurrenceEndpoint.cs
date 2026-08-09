using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Recurrences;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Recurrences;

internal static class DeleteRecurrenceEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/recurrences/{recurrenceId}", async (
            string recurrenceId,
            ArchiveWorkItemRecurrenceHandler handler,
            HttpContext http,
            CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new ArchiveWorkItemRecurrenceCommand(recurrenceId, CorrelationId(http)),
                ct);
            return Results.NoContent();
        }).WithZumboPermission(PermissionCatalog.WorkItemUpdate);
    }
}
