using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Recurrences;

internal static class ListTemplatesEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/templates", async (
            string projectId,
            int? page,
            int? pageSize,
            bool? includeArchived,
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ListTemplatesAsync(
                projectId, page ?? 1, pageSize ?? 50, includeArchived ?? false, ct), http));
    }
}
