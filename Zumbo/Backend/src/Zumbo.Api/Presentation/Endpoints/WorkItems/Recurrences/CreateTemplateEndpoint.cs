using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Recurrences;

internal static class CreateTemplateEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/templates", async (
            CreateWorkItemTemplateRequest request,
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
            Created(await service.CreateTemplateAsync(request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemCreate);
    }
}
