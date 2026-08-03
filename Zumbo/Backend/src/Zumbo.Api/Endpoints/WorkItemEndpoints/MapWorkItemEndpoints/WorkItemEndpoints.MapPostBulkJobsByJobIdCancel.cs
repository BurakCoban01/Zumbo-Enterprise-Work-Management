using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostBulkJobsByJobIdCancel(RouteGroupBuilder group){group.MapPost("/bulk/jobs/{jobId}/cancel", async (
            string jobId, WorkItemBulkJobService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.CancelAsync(jobId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
}}
