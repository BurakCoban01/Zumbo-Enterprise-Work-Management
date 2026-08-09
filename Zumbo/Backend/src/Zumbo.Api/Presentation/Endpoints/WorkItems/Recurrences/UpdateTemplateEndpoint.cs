using Zumbo.Modules.WorkItems;
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
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.UpdateTemplateAsync(templateId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
    }
}
