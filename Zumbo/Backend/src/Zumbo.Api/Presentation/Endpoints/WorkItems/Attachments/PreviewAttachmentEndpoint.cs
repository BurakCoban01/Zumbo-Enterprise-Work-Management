using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Attachments;

internal static class PreviewAttachmentEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id}/attachments/{attachmentId}/preview", async (
            string id,
            string attachmentId,
            OpenAttachmentHandler handler,
            HttpContext http,
            CancellationToken ct) =>
        {
            var attachment = await handler.HandleAsync(new OpenAttachmentQuery(id, attachmentId), ct);
            if (!IsPreviewableContentType(attachment.ContentType))
            {
                await attachment.Content.DisposeAsync();
                return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
            }

            http.Response.Headers.CacheControl = "private, no-store";
            http.Response.Headers.Pragma = "no-cache";
            http.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
            http.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
            http.Response.Headers.ContentDisposition =
                $"inline; filename*=UTF-8''{Uri.EscapeDataString(attachment.FileName)}";
            return Results.Stream(
                attachment.Content,
                attachment.ContentType,
                enableRangeProcessing: true);
        });
    }
}
