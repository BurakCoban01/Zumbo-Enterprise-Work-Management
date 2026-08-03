using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPutByIdCustomFields(RouteGroupBuilder group){group.MapPut("/{id}/custom-fields", async (
                string id,
                SetWorkItemCustomFieldsRequest request,
                WorkItemService service,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await service.SetCustomFieldsAsync(id, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
}}
