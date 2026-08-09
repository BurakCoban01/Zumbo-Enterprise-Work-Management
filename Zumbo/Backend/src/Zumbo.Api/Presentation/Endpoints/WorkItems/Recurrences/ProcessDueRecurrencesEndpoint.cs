using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Recurrences;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Recurrences;

internal static class ProcessDueRecurrencesEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/recurrences/process-due", async (
            ScheduleDueRecurrencesHandler handler,
            CancellationToken ct) =>
            Results.Ok(new
            {
                scheduled = await handler.HandleAsync(new ScheduleDueRecurrencesCommand(), ct)
            }))
            .WithZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true);
    }
}
