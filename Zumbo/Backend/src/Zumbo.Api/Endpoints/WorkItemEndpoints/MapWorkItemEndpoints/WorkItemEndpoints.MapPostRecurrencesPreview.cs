using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostRecurrencesPreview(RouteGroupBuilder group){group.MapPost("/recurrences/preview", async (
            PreviewWorkItemRecurrenceRequest request,
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.PreviewRecurrenceAsync(request, ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemCreate);
}}
