using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapGetRecurrences(RouteGroupBuilder group){group.MapGet("/recurrences", async (
            string projectId,
            int? page,
            int? pageSize,
            bool? includeArchived,
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ListRecurrencesAsync(
                projectId, page ?? 1, pageSize ?? 50, includeArchived ?? false, ct), http));
}}
