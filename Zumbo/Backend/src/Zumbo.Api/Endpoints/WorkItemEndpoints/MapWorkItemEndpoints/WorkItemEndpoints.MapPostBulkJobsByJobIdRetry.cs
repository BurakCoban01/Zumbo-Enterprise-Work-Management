using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostBulkJobsByJobIdRetry(RouteGroupBuilder group){group.MapPost("/bulk/jobs/{jobId}/retry", async (
            string jobId, WorkItemBulkJobService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.RetryAsync(jobId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
}}
