using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapGetByIdAttachmentsByAttachmentIdDownload(RouteGroupBuilder group){group.MapGet("/{id}/attachments/{attachmentId}/download", async (
            string id,
            string attachmentId,
            WorkItemService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            var attachment = await service.OpenAttachmentAsync(id, attachmentId, ct);
            http.Response.Headers.CacheControl = "private, no-store";
            http.Response.Headers.Pragma = "no-cache";
            return Results.File(
                attachment.Content,
                attachment.ContentType,
                attachment.FileName,
                enableRangeProcessing: true);
        });
}}
