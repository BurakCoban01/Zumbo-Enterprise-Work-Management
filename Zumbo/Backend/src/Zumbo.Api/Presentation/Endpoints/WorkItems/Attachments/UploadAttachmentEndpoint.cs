using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Attachments;

internal static class UploadAttachmentEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id}/attachments/upload", async (string id, IFormFile file, UploadAttachmentHandler handler, HttpContext http, CancellationToken ct) =>
        {
            await using var stream = file.OpenReadStream();
            return Ok(await handler.HandleAsync(
                new UploadAttachmentCommand(
                    id,
                    stream,
                    file.FileName,
                    file.ContentType,
                    file.Length,
                    CorrelationId(http)),
                ct), http);
        })
        .WithZumboPermission(PermissionCatalog.AttachmentCreate)
        .DisableAntiforgery()
        .RequireRateLimiting("upload");
    }
}
