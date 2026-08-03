using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPutTemplatesByTemplateId(RouteGroupBuilder group){group.MapPut("/templates/{templateId}", async (
            string templateId,
            UpdateWorkItemTemplateRequest request,
            WorkItemTemplateRecurrenceService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.UpdateTemplateAsync(templateId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
}}
