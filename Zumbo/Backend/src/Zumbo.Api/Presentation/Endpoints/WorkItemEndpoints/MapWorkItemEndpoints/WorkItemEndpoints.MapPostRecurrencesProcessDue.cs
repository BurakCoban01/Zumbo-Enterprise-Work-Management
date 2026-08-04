using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostRecurrencesProcessDue(RouteGroupBuilder group){group.MapPost("/recurrences/process-due", async (
            WorkItemTemplateRecurrenceService service,
            CancellationToken ct) =>
            Results.Ok(new { scheduled = await service.ScheduleDueAsync(ct) }))
            .WithZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true);
}}
