using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostBulkArchive(RouteGroupBuilder group){group.MapPost("/bulk/archive", async (BulkArchiveWorkItemsRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.BulkArchiveAsync(request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemDelete)
            .RequireRateLimiting("bulk");
}}
