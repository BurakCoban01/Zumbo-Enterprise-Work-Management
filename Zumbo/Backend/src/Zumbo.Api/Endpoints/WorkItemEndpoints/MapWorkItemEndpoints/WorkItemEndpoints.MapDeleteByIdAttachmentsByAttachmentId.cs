using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapDeleteByIdAttachmentsByAttachmentId(RouteGroupBuilder group){group.MapDelete("/{id}/attachments/{attachmentId}", async (string id, string attachmentId, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.DeleteAttachmentAsync(id, attachmentId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.AttachmentDelete);
}}
