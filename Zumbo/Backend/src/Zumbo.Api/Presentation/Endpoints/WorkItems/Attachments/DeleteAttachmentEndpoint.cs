using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Attachments;

internal static class DeleteAttachmentEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id}/attachments/{attachmentId}", async (string id, string attachmentId, DeleteAttachmentHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new DeleteAttachmentCommand(id, attachmentId, CorrelationId(http)),
                ct), http))
            .WithZumboPermission(PermissionCatalog.AttachmentDelete);
    }
}
