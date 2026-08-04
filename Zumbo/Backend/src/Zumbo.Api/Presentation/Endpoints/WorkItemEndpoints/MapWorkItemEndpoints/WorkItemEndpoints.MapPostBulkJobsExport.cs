using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostBulkJobsExport(RouteGroupBuilder group){group.MapPost("/bulk/jobs/export", async (
            CreateWorkItemExportJobRequest request,
            WorkItemBulkJobService service,
            HttpContext http,
            CancellationToken ct) =>
            Created(await service.SubmitExportAsync(request, IdempotencyKey(http), CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemView)
            .RequireRateLimiting("bulk");
}}
