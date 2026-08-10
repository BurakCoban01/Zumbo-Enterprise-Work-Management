using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Recurrences;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Recurrences;

internal static class UpdateTemplateEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/templates/{templateId}", async (
            string templateId,
            UpdateWorkItemTemplateRequest request,
            UpdateWorkItemTemplateHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new UpdateWorkItemTemplateCommand(templateId, request, CorrelationId(http)),
                ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
    }
}
