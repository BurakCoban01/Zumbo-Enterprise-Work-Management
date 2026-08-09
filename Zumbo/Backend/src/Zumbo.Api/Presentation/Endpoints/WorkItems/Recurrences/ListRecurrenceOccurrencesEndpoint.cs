using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Recurrences;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Recurrences;

internal static class ListRecurrenceOccurrencesEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/recurrences/{recurrenceId}/occurrences", async (
            string recurrenceId,
            int? page,
            int? pageSize,
            ListRecurrenceOccurrencesHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new ListRecurrenceOccurrencesQuery(
                    recurrenceId,
                    page ?? 1,
                    pageSize ?? 50),
                ct), http));
    }
}
