using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapGetRecurrencesByRecurrenceIdOccurrences(RouteGroupBuilder group){group.MapGet("/recurrences/{recurrenceId}/occurrences", async (
            string recurrenceId,
            int? page,
            int? pageSize,
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ListOccurrencesAsync(recurrenceId, page ?? 1, pageSize ?? 50, ct), http));
}}
