using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Boards.Application.Features.ColumnOrdering;
using Zumbo.Modules.Boards.Application.Features.Columns;

namespace Zumbo.Api.Presentation.Controllers.Boards;

[ApiController]
[Route("/api/boards/{boardId}/columns")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Boards")]
[ZumboPermission(PermissionCatalog.BoardManage)]
public sealed class BoardColumnsController : ApiControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create(
        [FromRoute] string boardId,
        [FromBody] CreateColumnRequest request,
        [FromServices] AddColumnHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            boardId,
            request,
            HttpContext.TraceIdentifier,
            cancellationToken));

    [HttpPut("{columnId}")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Update(
        [FromRoute] string boardId,
        [FromRoute] string columnId,
        [FromBody] UpdateColumnRequest request,
        [FromServices] UpdateColumnHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            boardId,
            columnId,
            request,
            HttpContext.TraceIdentifier,
            cancellationToken));

    [HttpPut("reorder")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Reorder(
        [FromRoute] string boardId,
        [FromBody] ReorderColumnsRequest request,
        [FromServices] ReorderColumnsHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            boardId,
            request,
            HttpContext.TraceIdentifier,
            cancellationToken));

    [HttpDelete("{columnId}")]
    public async Task<IActionResult> Delete(
        [FromRoute] string boardId,
        [FromRoute] string columnId,
        [FromServices] DeleteColumnHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new DeleteColumnCommand(boardId, columnId, HttpContext.TraceIdentifier),
            cancellationToken));
}
