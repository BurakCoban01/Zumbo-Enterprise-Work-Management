using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Attachments;

internal static class DownloadAttachmentEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id}/attachments/{attachmentId}/download", async (
            string id,
            string attachmentId,
            OpenAttachmentHandler handler,
            HttpContext http,
            CancellationToken ct) =>
        {
            var attachment = await handler.HandleAsync(new OpenAttachmentQuery(id, attachmentId), ct);
            http.Response.Headers.CacheControl = "private, no-store";
            http.Response.Headers.Pragma = "no-cache";
            return Results.File(
                attachment.Content,
                attachment.ContentType,
                attachment.FileName,
                enableRangeProcessing: true);
        });
    }
}
