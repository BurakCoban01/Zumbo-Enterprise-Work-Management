using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Projects.Application.Features.Knowledge;

namespace Zumbo.Api.Presentation.Controllers.Projects.Knowledge;

[ApiController]
[Route("/api/knowledge-documents/{documentId}/comments")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Knowledge")]
[ZumboPermission(PermissionCatalog.ProjectView)]
public sealed class KnowledgeCommentsController : ApiControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Add([FromRoute] string documentId, [FromBody] AddKnowledgeCommentRequest request, [FromServices] AddKnowledgeCommentHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new AddKnowledgeCommentCommand(documentId, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpPatch("{commentId}/resolve")]
    public async Task<IActionResult> Resolve([FromRoute] string documentId, [FromRoute] string commentId, [FromServices] ResolveKnowledgeCommentHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new ResolveKnowledgeCommentCommand(documentId, commentId, HttpContext.TraceIdentifier), cancellationToken));
}
