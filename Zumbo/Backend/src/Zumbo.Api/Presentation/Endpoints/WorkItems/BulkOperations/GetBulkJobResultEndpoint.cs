using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.BulkOperations;

internal static class GetBulkJobResultEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/bulk/jobs/{jobId}/result", async (
            string jobId, WorkItemBulkJobService service, CancellationToken ct) =>
        {
            var file = await service.OpenArtifactAsync(jobId, errors: false, ct);
            return Results.File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: false);
        });
    }
}
