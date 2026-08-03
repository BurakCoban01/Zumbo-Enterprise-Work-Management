using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapGetByIdAttachmentsByAttachmentIdPreview(RouteGroupBuilder group){group.MapGet("/{id}/attachments/{attachmentId}/preview", async (
            string id,
            string attachmentId,
            WorkItemService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            var attachment = await service.OpenAttachmentAsync(id, attachmentId, ct);
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
}}
