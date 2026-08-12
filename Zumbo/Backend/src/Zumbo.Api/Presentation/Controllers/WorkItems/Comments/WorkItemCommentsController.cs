using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Comments;

[ApiController]
[Route("/api/work-items/{id}/comments")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItems")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class WorkItemCommentsController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromRoute] string id,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromServices] WorkItemActivityQueryService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ListCommentsAsync(id, page ?? 1, pageSize ?? 50, cancellationToken));

    [HttpGet("{commentId}/revisions")]
    public async Task<IActionResult> ListRevisions(
        [FromRoute] string id,
        [FromRoute] string commentId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromServices] WorkItemActivityQueryService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ListRevisionsAsync(id, commentId, page ?? 1, pageSize ?? 50, cancellationToken));

    [HttpPost]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.CommentCreate)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Add(
        [FromRoute] string id,
        [FromBody] AddCommentRequest request,
        [FromServices] AddCommentHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new AddCommentCommand(id, request, HttpContext.TraceIdentifier),
            cancellationToken));

    [HttpPut("{commentId}")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.CommentCreate)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Edit(
        [FromRoute] string id,
        [FromRoute] string commentId,
        [FromBody] EditCommentRequest request,
        [FromServices] EditCommentHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new EditCommentCommand(id, commentId, request, HttpContext.TraceIdentifier),
            cancellationToken));

    [HttpDelete("{commentId}")]
    [ZumboPermission(PermissionCatalog.CommentCreate)]
    public async Task<IActionResult> Delete(
        [FromRoute] string id,
        [FromRoute] string commentId,
        [FromServices] DeleteCommentHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new DeleteCommentCommand(id, commentId, HttpContext.TraceIdentifier),
            cancellationToken));
}
