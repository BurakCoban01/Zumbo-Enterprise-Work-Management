using Zumbo.Modules.WorkItems;
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
            WorkItemTemplateRecurrenceService service,
            CancellationToken ct) =>
            Results.Ok(new { scheduled = await service.ScheduleDueAsync(ct) }))
            .WithZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true);
    }
}
