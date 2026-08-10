using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;
using static Zumbo.Api.Presentation.Endpoints.WorkItems.BulkOperations.BulkOperationEndpointHeaders;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.BulkOperations;

internal static class CreateBulkImportJobEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk/jobs/import", async (
            CreateWorkItemImportJobRequest request,
            WorkItemBulkJobService service,
            HttpContext http,
            CancellationToken ct) =>
            Created(await service.SubmitImportAsync(request, IdempotencyKey(http), CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemCreate)
            .RequireRateLimiting("bulk");
    }
}
