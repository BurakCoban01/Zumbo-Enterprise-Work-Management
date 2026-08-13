using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Boards.Application.Features.Swimlanes;

namespace Zumbo.Api.Presentation.Controllers.Boards;

[ApiController]
[Route("/api/boards/{boardId}")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Boards")]
[ZumboPermission(PermissionCatalog.BoardManage)]
public sealed class BoardConfigurationController : ApiControllerBase
{
    [HttpPatch("swimlane")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> UpdateSwimlane(
        [FromRoute] string boardId,
        [FromBody] UpdateSwimlaneRequest request,
        [FromServices] UpdateSwimlaneHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            boardId,
            request,
            HttpContext.TraceIdentifier,
            cancellationToken));

    [HttpPut("workflow-mapping")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> ConfigureWorkflowMapping(
        [FromRoute] string boardId,
        [FromBody] ConfigureBoardWorkflowMappingRequest request,
        [FromServices] BoardWorkflowMappingService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ConfigureAsync(
            boardId,
            request,
            HttpContext.TraceIdentifier,
            cancellationToken));
}
