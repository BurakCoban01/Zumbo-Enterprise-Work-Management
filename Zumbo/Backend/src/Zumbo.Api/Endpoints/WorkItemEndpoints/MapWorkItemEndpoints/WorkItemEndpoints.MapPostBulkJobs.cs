using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostBulkJobs(RouteGroupBuilder group){group.MapPost("/bulk/jobs", async (
            CreateWorkItemBulkJobRequest request,
            WorkItemBulkJobService service,
            HttpContext http,
            CancellationToken ct) =>
            Created(await service.SubmitBulkAsync(request, IdempotencyKey(http), CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate)
            .RequireRateLimiting("bulk");
}}
