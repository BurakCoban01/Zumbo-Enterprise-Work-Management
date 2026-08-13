using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Boards.Application.Features.BoardsCore;

namespace Zumbo.Api.Presentation.Controllers.Boards;

[ApiController]
[Route("/api/boards")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Boards")]
[ZumboPermission(PermissionCatalog.BoardView)]
public sealed class BoardCatalogController : ApiControllerBase
{
    [HttpGet("by-project/{projectId}")]
    public async Task<IActionResult> ListByProject(
        [FromRoute] string projectId,
        [FromQuery] bool? archived,
        [FromServices] ListBoardsByProjectHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new ListBoardsByProjectQuery(projectId, archived ?? false),
            cancellationToken));

    [HttpPost]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.BoardManage)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create(
        [FromBody] CreateBoardRequest request,
        [FromServices] CreateBoardHandler handler,
        CancellationToken cancellationToken) =>
        CreatedEnvelopeResult(await handler.HandleAsync(
            request,
            HttpContext.TraceIdentifier,
            cancellationToken));

    [HttpPut("{boardId}")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.BoardManage)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Update(
        [FromRoute] string boardId,
        [FromBody] UpdateBoardRequest request,
        [FromServices] UpdateBoardHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            boardId,
            request,
            HttpContext.TraceIdentifier,
            cancellationToken));
}
