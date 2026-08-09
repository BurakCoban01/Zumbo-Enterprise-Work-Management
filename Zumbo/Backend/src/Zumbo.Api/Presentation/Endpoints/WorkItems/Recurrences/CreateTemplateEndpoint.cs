using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Recurrences;
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
            CreateWorkItemTemplateHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Created(await handler.HandleAsync(
                new CreateWorkItemTemplateCommand(request, CorrelationId(http)),
                ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemCreate);
    }
}
