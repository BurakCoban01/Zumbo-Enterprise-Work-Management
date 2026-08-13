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
[Route("/api/knowledge-documents")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Knowledge")]
[ZumboPermission(PermissionCatalog.ProjectView)]
public sealed class KnowledgeDocumentsController : ApiControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create([FromBody] CreateKnowledgeDocumentRequest request, [FromServices] CreateKnowledgeDocumentHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new CreateKnowledgeDocumentCommand(request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpPut("{documentId}")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> AddVersion([FromRoute] string documentId, [FromBody] CreateKnowledgeVersionRequest request, [FromServices] AddKnowledgeVersionHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new AddKnowledgeVersionCommand(documentId, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpDelete("{documentId}")]
    public async Task<IActionResult> Archive([FromRoute] string documentId, [FromServices] ArchiveKnowledgeDocumentHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(new ArchiveKnowledgeDocumentCommand(documentId, HttpContext.TraceIdentifier), cancellationToken);
        return OkEnvelopeResult(new { archived = true });
    }
}
