using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Schema;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Schema;

[ApiController]
[Route("/api/work-item-schemas")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItemSchemas")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class WorkItemSchemasController : ApiControllerBase
{
    [HttpGet("{projectId}")]
    public async Task<IActionResult> Get([FromRoute] string projectId, [FromServices] GetWorkItemTypeSchemaHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new GetWorkItemTypeSchemaQuery(projectId), cancellationToken));

    [HttpPut("{projectId}")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemUpdate)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Upsert(
        [FromRoute] string projectId,
        [FromBody] UpsertWorkItemTypeSchemaRequest request,
        [FromServices] UpsertWorkItemTypeSchemaHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new UpsertWorkItemTypeSchemaCommand(projectId, request, HttpContext.TraceIdentifier),
            cancellationToken));

    [HttpGet("{projectId}/reports/issue-types")]
    public async Task<IActionResult> GetIssueTypeDistribution(
        [FromRoute] string projectId,
        [FromServices] GetIssueTypeDistributionHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new GetIssueTypeDistributionQuery(projectId), cancellationToken));

    [HttpGet("{projectId}/reports/custom-fields/{fieldKey}")]
    public async Task<IActionResult> GetCustomFieldDistribution(
        [FromRoute] string projectId,
        [FromRoute] string fieldKey,
        [FromServices] GetCustomFieldDistributionHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new GetCustomFieldDistributionQuery(projectId, fieldKey),
            cancellationToken));
}
