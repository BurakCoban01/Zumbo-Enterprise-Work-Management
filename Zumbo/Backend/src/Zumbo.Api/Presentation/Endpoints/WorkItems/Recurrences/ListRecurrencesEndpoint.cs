using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Recurrences;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Recurrences;

internal static class ListRecurrencesEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/recurrences", async (
            string projectId,
            int? page,
            int? pageSize,
            bool? includeArchived,
            ListWorkItemRecurrencesHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new ListWorkItemRecurrencesQuery(
                    projectId,
                    page ?? 1,
                    pageSize ?? 50,
                    includeArchived ?? false),
                ct), http));
    }
}
