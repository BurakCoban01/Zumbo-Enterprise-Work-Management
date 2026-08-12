using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Boards.Application.Features.Lifecycle;

namespace Zumbo.Api.Presentation.Controllers.Boards;

[ApiController]
[Route("/api/boards")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Boards")]
[ZumboPermission(PermissionCatalog.BoardView)]
public sealed class BoardLifecycleController : ApiControllerBase
{
    [HttpDelete("{boardId}")]
    [ZumboPermission(PermissionCatalog.BoardManage)]
    public async Task<IActionResult> Archive(
        [FromRoute] string boardId,
        [FromServices] ArchiveBoardHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(
            new ArchiveBoardCommand(boardId, HttpContext.TraceIdentifier),
            cancellationToken);
        return OkEnvelopeResult(new { archived = true });
    }

    [HttpPost("{boardId}/restore")]
    [ZumboPermission(PermissionCatalog.BoardManage)]
    public async Task<IActionResult> Restore(
        [FromRoute] string boardId,
        [FromServices] RestoreBoardHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new RestoreBoardCommand(boardId, HttpContext.TraceIdentifier),
            cancellationToken));
}
