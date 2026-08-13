using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects.Application.Features.Knowledge;

namespace Zumbo.Api.Presentation.Controllers.Projects.Knowledge;

[ApiController]
[Route("/api/knowledge-documents")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Knowledge")]
[ZumboPermission(PermissionCatalog.ProjectView)]
public sealed class KnowledgeQueriesController : ApiControllerBase
{
    [HttpGet]
    [EnableRateLimiting("search")]
    public async Task<IActionResult> Search([FromQuery] string? query, [FromQuery] string? scopeType, [FromQuery] string? scopeId, [FromQuery] bool? includeArchived, [FromQuery] int? page, [FromQuery] int? pageSize, [FromServices] SearchKnowledgeDocumentsHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new SearchKnowledgeDocumentsQuery(query, scopeType, scopeId, includeArchived ?? false, page ?? 1, pageSize ?? 50), cancellationToken));

    [HttpGet("{documentId}")]
    public async Task<IActionResult> Get([FromRoute] string documentId, [FromQuery] bool? includeArchived, [FromServices] GetKnowledgeDocumentHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new GetKnowledgeDocumentQuery(documentId, includeArchived ?? false), cancellationToken));

    [HttpGet("scope-link-options")]
    [EnableRateLimiting("search")]
    public async Task<IActionResult> GetScopeLinkOptions([FromQuery, BindRequired] string scopeType, [FromQuery, BindRequired] string scopeId, [FromQuery] string? query, [FromServices] GetKnowledgeLinkOptionsHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new GetKnowledgeLinkOptionsQuery(scopeType, scopeId, query), cancellationToken));

    [HttpGet("{documentId}/versions/{number:int}")]
    public async Task<IActionResult> GetVersion([FromRoute] string documentId, [FromRoute] int number, [FromServices] GetKnowledgeVersionHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new GetKnowledgeVersionQuery(documentId, number), cancellationToken));
}
