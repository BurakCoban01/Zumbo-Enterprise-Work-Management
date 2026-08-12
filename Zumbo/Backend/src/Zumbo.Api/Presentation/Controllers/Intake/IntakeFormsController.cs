using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.Intake;

[ApiController]
[Route("/api/intake/forms")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Intake")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class IntakeFormsController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery, BindRequired] string projectId, [FromServices] IntakeFormService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ListAsync(projectId, cancellationToken));

    [HttpGet("{formId}")]
    public async Task<IActionResult> Get([FromRoute] string formId, [FromServices] IntakeFormService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.GetAsync(formId, cancellationToken));

    [HttpPost]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkflowManage)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create([FromBody] CreateIntakeFormRequest request, [FromServices] IntakeFormService service, CancellationToken cancellationToken) =>
        CreatedEnvelopeResult(await service.CreateAsync(request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPut("{formId}")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkflowManage)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Update([FromRoute] string formId, [FromBody] UpdateIntakeFormRequest request, [FromServices] IntakeFormService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.UpdateAsync(formId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("{formId}/publish")]
    [ZumboPermission(PermissionCatalog.WorkflowManage)]
    public async Task<IActionResult> Publish([FromRoute] string formId, [FromServices] IntakeFormService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.PublishAsync(formId, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("{formId}/archive")]
    [ZumboPermission(PermissionCatalog.WorkflowManage)]
    public async Task<IActionResult> Archive([FromRoute] string formId, [FromServices] IntakeFormService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ArchiveAsync(formId, HttpContext.TraceIdentifier, cancellationToken));

    [HttpGet("{formId}/published")]
    [ZumboPermission(PermissionCatalog.WorkItemCreate)]
    public async Task<IActionResult> GetPublished([FromRoute] string formId, [FromServices] IntakeFormService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.GetPublishedAsync(formId, publicAccess: false, cancellationToken));
}
