using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Boards.Application.Features.Views;

namespace Zumbo.Api.Presentation.Controllers.Boards;

[ApiController]
[Route("/api/boards/{boardId}/views")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Boards")]
[ZumboPermission(PermissionCatalog.BoardManage)]
public sealed class BoardViewsController : ApiControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create(
        [FromRoute] string boardId,
        [FromBody] CreateBoardViewRequest request,
        [FromServices] CreateViewHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            boardId,
            request,
            HttpContext.TraceIdentifier,
            cancellationToken));

    [HttpPut("{viewId}")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Update(
        [FromRoute] string boardId,
        [FromRoute] string viewId,
        [FromBody] UpdateBoardViewRequest request,
        [FromServices] UpdateViewHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            boardId,
            viewId,
            request,
            HttpContext.TraceIdentifier,
            cancellationToken));

    [HttpDelete("{viewId}")]
    public async Task<IActionResult> Delete(
        [FromRoute] string boardId,
        [FromRoute] string viewId,
        [FromServices] DeleteViewHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            boardId,
            viewId,
            HttpContext.TraceIdentifier,
            cancellationToken));
}
