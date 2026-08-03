using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostRecurrences(RouteGroupBuilder group){group.MapPost("/recurrences", async (
            CreateWorkItemRecurrenceRequest request,
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
            Created(await service.CreateRecurrenceAsync(request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemCreate);
}}
