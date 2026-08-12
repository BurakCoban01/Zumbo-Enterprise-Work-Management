using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Attachments;

[ApiController]
[Route("/api/work-items/{id}/attachments")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItems")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class WorkItemAttachmentsController : ApiControllerBase
{
    [HttpPost("upload")]
    [DisableAntiforgeryForController]
    [EnableRateLimiting("upload")]
    [ZumboPermission(PermissionCatalog.AttachmentCreate)]
    public async Task<IActionResult> Upload([FromRoute] string id, [BindRequired] IFormFile file, [FromServices] UploadAttachmentHandler handler, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        return OkEnvelopeResult(await handler.HandleAsync(
            new UploadAttachmentCommand(id, stream, file.FileName, file.ContentType, file.Length, HttpContext.TraceIdentifier),
            cancellationToken));
    }

    [HttpGet("{attachmentId}/preview")]
    public async Task<IActionResult> Preview([FromRoute] string id, [FromRoute] string attachmentId, [FromServices] OpenAttachmentHandler handler, CancellationToken cancellationToken)
    {
        var attachment = await handler.HandleAsync(new OpenAttachmentQuery(id, attachmentId), cancellationToken);
        if (!ApiEndpointResults.IsPreviewableContentType(attachment.ContentType))
        {
            await attachment.Content.DisposeAsync();
            return StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
        Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
        Response.Headers.ContentDisposition = $"inline; filename*=UTF-8''{Uri.EscapeDataString(attachment.FileName)}";
        return File(attachment.Content, attachment.ContentType, enableRangeProcessing: true);
    }

    [HttpGet("{attachmentId}/download")]
    public async Task<IActionResult> Download([FromRoute] string id, [FromRoute] string attachmentId, [FromServices] OpenAttachmentHandler handler, CancellationToken cancellationToken)
    {
        var attachment = await handler.HandleAsync(new OpenAttachmentQuery(id, attachmentId), cancellationToken);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.Pragma = "no-cache";
        return File(attachment.Content, attachment.ContentType, attachment.FileName, enableRangeProcessing: true);
    }

    [HttpDelete("{attachmentId}")]
    [ZumboPermission(PermissionCatalog.AttachmentDelete)]
    public async Task<IActionResult> Delete([FromRoute] string id, [FromRoute] string attachmentId, [FromServices] DeleteAttachmentHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new DeleteAttachmentCommand(id, attachmentId, HttpContext.TraceIdentifier), cancellationToken));
}
