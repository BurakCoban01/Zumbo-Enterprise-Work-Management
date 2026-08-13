using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Recurrences;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Recurrences;

[ApiController]
[Route("/api/work-items/templates")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItems")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class WorkItemTemplatesController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery, BindRequired] string projectId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] bool? includeArchived,
        [FromServices] ListWorkItemTemplatesHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new ListWorkItemTemplatesQuery(projectId, page ?? 1, pageSize ?? 50, includeArchived ?? false),
            cancellationToken));

    [HttpPost]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemCreate)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create(
        [FromBody] CreateWorkItemTemplateRequest request,
        [FromServices] CreateWorkItemTemplateHandler handler,
        CancellationToken cancellationToken) =>
        CreatedEnvelopeResult(await handler.HandleAsync(
            new CreateWorkItemTemplateCommand(request, HttpContext.TraceIdentifier),
            cancellationToken));

    [HttpPut("{templateId}")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemUpdate)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Update(
        [FromRoute] string templateId,
        [FromBody] UpdateWorkItemTemplateRequest request,
        [FromServices] UpdateWorkItemTemplateHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new UpdateWorkItemTemplateCommand(templateId, request, HttpContext.TraceIdentifier),
            cancellationToken));

    [HttpDelete("{templateId}")]
    [ZumboPermission(PermissionCatalog.WorkItemUpdate)]
    public async Task<IActionResult> Archive(
        [FromRoute] string templateId,
        [FromServices] ArchiveWorkItemTemplateHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(
            new ArchiveWorkItemTemplateCommand(templateId, HttpContext.TraceIdentifier),
            cancellationToken);
        return NoContent();
    }
}
