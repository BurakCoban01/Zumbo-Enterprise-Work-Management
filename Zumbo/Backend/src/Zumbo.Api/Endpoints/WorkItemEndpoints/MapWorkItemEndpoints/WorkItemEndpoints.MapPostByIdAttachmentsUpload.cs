using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostByIdAttachmentsUpload(RouteGroupBuilder group){group.MapPost("/{id}/attachments/upload", async (string id, IFormFile file, WorkItemService service, HttpContext http, CancellationToken ct) =>
        {
            await using var stream = file.OpenReadStream();
            return Ok(await service.UploadAttachmentAsync(
                id,
                stream,
                file.FileName,
                file.ContentType,
                file.Length,
                CorrelationId(http),
                ct), http);
        })
        .WithZumboPermission(PermissionCatalog.AttachmentCreate)
        .DisableAntiforgery()
        .RequireRateLimiting("upload");
}}
